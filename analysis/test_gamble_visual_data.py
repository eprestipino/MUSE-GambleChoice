"""Visualization provenance, analysis exports and malformed-data regressions."""
import copy
import csv
import json
import tempfile
import unittest
from pathlib import Path

from analyze_gamble_choice import analyze

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / 'fixtures/schema2-model.jsonl'


class VisualDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.records = []
        with FIXTURE.open(encoding='utf-8') as source:
            for line in source:
                row = json.loads(line)
                cls.records.append(row)
                if row['kind'] == 'trial':
                    break

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def report(self, records):
        journal = self.root / 'events.jsonl'
        journal.write_text(''.join(json.dumps(r) + '\n' for r in records), encoding='utf-8')
        return analyze(journal, self.root / 'analysis')

    def test_schema2_exports_preserve_response_timing_and_visual_provenance(self):
        report = self.report(self.records)
        self.assertTrue(report['passed'], report['issues'])
        final = self.records[-1]['data']
        with (self.root / 'analysis/trials.csv').open(newline='', encoding='utf-8') as source:
            row = next(csv.DictReader(source))
        for column, field in [('contact_rt_ms', 'FirstContactRTMs'), ('commit_rt_ms', 'CommitRTMs'),
                              ('reveal_late_ms', 'RevealLateMs')]:
            self.assertIn(column, row)
            self.assertEqual(str(final[field]), row[column])
        self.assertEqual(str(final['Config']['ItiMs']), row['iti_ms'])
        with (self.root / 'analysis/outcomes.csv').open(newline='', encoding='utf-8') as source:
            rows = list(csv.DictReader(source))
        for row in rows:
            self.assertIn('color', row)
            self.assertIn('probability_representation', row)
            self.assertIn('reward_representation', row)

    def test_export_event_trial_id_collision_rejects_without_crash(self):
        records = copy.deepcopy(self.records)
        next(r['data'] for r in records if r['kind'] == 'event')['trial_id'] = 'spoofed'
        self.assertFalse(self.report(records)['passed'])

    def test_oversized_numeric_values_reject_without_crash(self):
        for field in ('OnsetMs', 'CommitRTMs'):
            with self.subTest(field=field):
                records = copy.deepcopy(self.records)
                records[-1]['data'][field] = 10 ** 500
                self.assertFalse(self.report(records)['passed'])

    def test_boolean_or_fractional_outcome_index_rejects(self):
        for field in ('OutcomeIndex', 'Tokens'):
            current = self.records[-1]['data'][field]
            for value in (False, True, float(current)):
                records = copy.deepcopy(self.records)
                current = records[-1]['data'][field]
                if current != value:
                    continue
                with self.subTest(field=field, value=value):
                    records[-1]['data'][field] = value
                    self.assertFalse(self.report(records)['passed'])

    def test_invalid_resolved_color_rejects(self):
        for color in ('not-a-color', '#12', 123, [], {}):
            with self.subTest(color=color):
                records = copy.deepcopy(self.records)
                for record in records:
                    if record['kind'] in ('prepared', 'trial'):
                        record['data']['Options'][0]['Outcomes'][0]['Color'] = color
                self.assertFalse(self.report(records)['passed'])

    def test_display_configuration_corruptions_reject(self):
        for field, value in [('Renderer', 'Unknown'), ('PaletteMode', 'Unknown'),
                             ('ItiMode', 'Unknown'), ('Background', '#12'),
                             ('LowColors', []), ('HighColors', ['red', 'blue']),
                             ('SwapRoles', 1), ('GridCells', True), ('GridCells', 101)]:
            with self.subTest(field=field, value=value):
                records = copy.deepcopy(self.records)
                for record in records:
                    if record['kind'] in ('prepared', 'trial'):
                        record['data']['Config'][field] = value
                self.assertFalse(self.report(records)['passed'])

    def test_fallback_and_explicit_colors_export_correctly(self):
        records = copy.deepcopy(self.records)
        for record in records:
            if record['kind'] in ('prepared', 'trial'):
                for option in record['data']['Options']:
                    option['Outcomes'][0]['Color'] = '#123aBC'
        self.assertTrue(self.report(records)['passed'])
        with (self.root / 'analysis/outcomes.csv').open(newline='', encoding='utf-8') as source:
            rows = list(csv.DictReader(source))
        expected = ('#649ECC', '#C48B55', '#77AA77', '#AD85C4', '#EEC75E', '#DB8398')
        index = 0
        for option in records[-1]['data']['Options']:
            for outcome_index, outcome in enumerate(option['Outcomes']):
                color = outcome.get('Color') or expected[(outcome_index + 3 * option['Palette']) % 6]
                self.assertEqual(color, rows[index]['color'])
                self.assertEqual(option['ProbabilityRepresentation'], rows[index]['probability_representation'])
                self.assertEqual(option['RewardRepresentation'], rows[index]['reward_representation'])
                index += 1

    def test_legacy_display_and_observed_pulse_corruptions_reject(self):
        fixture = ROOT / 'fixtures/legacy-model.jsonl'
        record = json.loads(fixture.read_text(encoding='utf-8').splitlines()[0])
        for field, value in [('Renderer', 'Unknown'), ('LowColors', []), ('ObservedPulses', []),
                             ('ObservedPulses', True), ('ObservedPulses', -1)]:
            with self.subTest(field=field):
                mutated = copy.deepcopy(record)
                target = mutated['data'] if field == 'ObservedPulses' else mutated['data']['Config']
                target[field] = value
                path = self.root / 'legacy.jsonl'
                path.write_text(json.dumps(mutated) + '\n', encoding='utf-8')
                self.assertFalse(analyze(path, self.root / 'legacy-analysis', model_fixtures=True)['passed'])

    def test_mixed_schema_exports_keep_shared_response_columns(self):
        fixture = ROOT / 'fixtures/legacy-model.jsonl'
        legacy = json.loads(fixture.read_text(encoding='utf-8').splitlines()[0])
        path = self.root / 'mixed.jsonl'
        path.write_text(''.join(json.dumps(r) + '\n' for r in [legacy, *self.records]), encoding='utf-8')
        report = analyze(path, self.root / 'mixed-analysis', model_fixtures=True)
        self.assertTrue(report['passed'], report['issues'])
        with (self.root / 'mixed-analysis/trials.csv').open(newline='', encoding='utf-8') as source:
            rows = list(csv.DictReader(source))
        self.assertEqual(2, len(rows))
        for row, original in zip(rows, (legacy['data'], self.records[-1]['data'])):
            self.assertEqual(float(row['commit_rt_ms']), original['CommitRTMs'])
            self.assertEqual(float(row['iti_ms']), original['Config']['ItiMs'])


if __name__ == '__main__':
    unittest.main()
