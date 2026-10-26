"""Validate GambleChoice JSONL and export trial/options CSVs; preserves incomplete trials."""
import argparse
import csv
import json
import math
import re
from pathlib import Path

def _number(value):
    return type(value) in (int, float) and math.isfinite(value)


def validate_visual_config(config, schema):
    representations = {'Bar', 'Pie'} if schema == 1 else {'Bar','Pie','HorizontalBar','Numeric','Grid','Donut'}
    if not isinstance(config['Renderer'],str) or config['Renderer'] not in representations:
        raise ValueError('invalid probability renderer')
    if config['PaletteMode'] not in ('Fixed','Alternating','Random'):
        raise ValueError('invalid palette policy')
    if config['ItiMode'] not in ('Static','Remaining','Elapsed'):
        raise ValueError('invalid ITI display mode')
    if type(config['SwapRoles']) is not bool:
        raise ValueError('invalid role-color swap')
    colors = []
    for field in ('LowColors','HighColors'):
        if not isinstance(config[field],list) or len(config[field]) != 2:
            raise ValueError('invalid palette colors')
        colors.extend(config[field])
    colors.extend(config[field] for field in ('Background','ActualizedColor','ItiColor','IdleColor','HoldColor','CommittedColor'))
    if any(not isinstance(color,str) or re.fullmatch(r'#[0-9a-fA-F]{6}',color) is None for color in colors):
        raise ValueError('invalid display color')

def strict_json(text):
    def unique_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError(f'duplicate JSON key: {key}')
            result[key] = value
        return result
    def reject_constant(value): raise ValueError(f'nonfinite JSON number: {value}')
    return json.loads(text, object_pairs_hook=unique_pairs, parse_constant=reject_constant)

def replay_draw(seed, trial):
    state = ((seed & 0xffffffff) ^ ((trial * 0x9E3779B9) & 0xffffffff) ^ 0x1234ABCD) or 0xA341316C
    state ^= (state << 13) & 0xffffffff
    state ^= state >> 17
    state ^= (state << 5) & 0xffffffff
    return (state & 0xffffffff) / 4294967296


def analyze(path, output, model_fixtures=False):
    rows, prepared, events, checkpoints, issues = {}, {}, {}, {}, []
    try:
        lines = path.read_text(encoding='utf-8-sig').splitlines()
    except UnicodeDecodeError:
        lines = []
        issues.append('Invalid UTF-8 journal')
    if not lines: issues.append('Empty journal: no trials recorded')
    for index, line in enumerate(lines):
        try:
            obj = strict_json(line)
        except (ValueError, json.JSONDecodeError):
            issues.append(f'Invalid JSON at line {index+1}' + (' (truncated final line)' if index == len(lines)-1 else ''))
            continue
        if not isinstance(obj, dict) or not isinstance(obj.get('data'), dict):
            issues.append(f'Invalid record structure at line {index+1}')
            continue
        kind, data = obj.get('kind'), obj['data']
        if not isinstance(kind,str) or kind not in {'prepared', 'checkpoint', 'event', 'trial', 'fault'}:
            issues.append(f'Unknown record kind at line {index+1}')
            continue
        key = data.get('TrialId')
        if not isinstance(key,str) or not key:
            issues.append(f'Missing trial ID at line {index+1}')
            continue
        if kind == 'fault': issues.append(f'{key}: runtime fault: {data.get("Reason", "unspecified")}')
        elif kind == 'prepared':
            if key in prepared: issues.append(f'Duplicate prepared trial {key}')
            prepared[key] = data
        elif kind == 'checkpoint': checkpoints[key] = data
        elif kind == 'event': events.setdefault(key, []).append(data)
        elif kind == 'trial':
            if key in rows: issues.append(f'Duplicate final trial {key}')
            rows[key] = data
    trials, options, outcomes, ledger_rows = [], [], [], []
    from gamble_schema2 import validate_v2
    ledger_state = {}; adaptive_state = {}
    for key, r in rows.items():
        if r.get('SchemaVersion') == 2:
            try:
                validate_v2(r, prepared.get(key), events.get(key, []), model_fixtures,
                            issues, trials, options, outcomes, ledger_rows, ledger_state, adaptive_state)
            except (KeyError, TypeError, ValueError, IndexError, OverflowError, StopIteration) as ex:
                issues.append(f'{key}: invalid schema 2 trial structure: {ex}')
            continue
        try:
            _validate_record(r)
        except (KeyError, TypeError, ValueError, OverflowError) as ex:
            issues.append(f'{key}: invalid trial structure: {ex}')
            continue
        c = r['Config']; offered = r['Options']; chosen = r['ChosenIndex']; amount = r.get('Tokens')
        if r['SchemaVersion'] != 1 or c['SchemaVersion'] != 1: issues.append(f'{key}: unsupported schema version')
        if r['Status'] not in {'Completed', 'Omission', 'Aborted'}: issues.append(f'{key}: invalid final status')
        if r['RandomAlgorithm'] != 'xorshift32-v1': issues.append(f'{key}: unsupported random algorithm')
        if r.get('Draw') is not None and r['Draw'] != replay_draw(c['Seed'], r['TrialIndex']): issues.append(f'{key}: seed/draw replay mismatch')
        if r['EndMs'] < r['OnsetMs']: issues.append(f'{key}: negative trial duration')
        if not model_fixtures and key not in prepared: issues.append(f'{key}: missing prepared record')
        if key in prepared and any(prepared[key].get(n)!=r[n] for n in ('Config','Options','OnsetMs','TrialIndex')): issues.append(f'{key}: prepared/final configuration changed')
        if len(offered) != c['ChoiceCount'] or len({o['Id'] for o in offered}) != len(offered): issues.append(f'{key}: invalid option identities/count')
        for pos, o in enumerate(offered):
            p, lo, hi = o['PHigh'], o['Low'], o['High']
            if not (0 <= p <= 1 and 0 <= lo <= hi <= 6) or not isinstance(lo,int) or not isinstance(hi,int): issues.append(f'{key}: invalid lottery')
            palette=o['Palette']
            options.append(dict(trial_id=key, position=pos, option_id=o['Id'], low=lo, high=hi, p_high=p,
                                ev=lo+p*(hi-lo), variance=p*(1-p)*(hi-lo)**2, palette=palette, chosen=pos==chosen,
                                low_color=c['HighColors' if c['SwapRoles'] else 'LowColors'][palette],
                                high_color=c['LowColors' if c['SwapRoles'] else 'HighColors'][palette],
                                probability_representation=c['Renderer'],reward_representation='TokenSlots'))
        if r['Status'] == 'Completed':
            if not (0 <= chosen < len(offered)):
                issues.append(f'{key}: completed without valid choice')
            else:
                o = offered[chosen]; role = 'High' if r['Draw'] < o['PHigh'] else 'Low'
                if not 0 <= r['Draw'] < 1: issues.append(f'{key}: draw outside half-open unit interval')
                if r['CommitRTMs'] is None or not c['HoldMs'] <= r['CommitRTMs'] <= c['DeadlineMs']: issues.append(f'{key}: invalid commit timing')
                if r['Role'] != role or amount != o[role] or r['ChosenId'] != o['Id']: issues.append(f'{key}: outcome mismatch')
                if r['RequestedPulses'] != amount*c['PulsesPerToken']: issues.append(f'{key}: pulse mapping mismatch')
                if amount == 0 and r['RewardStatus'] != 'ZeroNoCommand': issues.append(f'{key}: zero reward not explicitly suppressed')
                if amount and r['RewardStatus'] not in {'Simulated', 'CommandsQueuedNotMeasured'}: issues.append(f'{key}: completed reward not settled')
                if amount and c['PhysicalReward'] != (r['RewardStatus']=='CommandsQueuedNotMeasured'): issues.append(f'{key}: reward mode/status mismatch')
                if r['RevealActualMs'] < r['OnsetMs']+c['RevealMs']: issues.append(f'{key}: early reveal')
                if r['TransferEndMs']+0.001 < r['RevealActualMs']+c['HighlightMs']+c['TransferMs']: issues.append(f'{key}: short animation window')
                if r['ItiStartMs']+0.001 < r['TransferEndMs']+c['RewardWindowMs']: issues.append(f'{key}: short reward window')
                if r['EndMs']+0.001 < r['ItiStartMs']+c['ItiMs']: issues.append(f'{key}: short ITI')
        if r['Status'] == 'Omission' and (amount is not None or chosen != -1 or r['RequestedPulses'] or r.get('Draw') is not None or r.get('Role') is not None or r.get('CommitRTMs') is not None): issues.append(f'{key}: omission contains outcome/reward')
        if r['Status']=='Aborted' and chosen>=0:
            if chosen>=len(offered): issues.append(f'{key}: aborted trial has invalid choice')
            elif r.get('Draw') is None: issues.append(f'{key}: committed aborted trial has no draw')
            else:
                o=offered[chosen];role='High' if r['Draw']<o['PHigh'] else 'Low'
                if r.get('Role')!=role or amount!=o[role] or r.get('ChosenId')!=o['Id']: issues.append(f'{key}: aborted outcome mismatch')
        evs = events.get(key, [])
        if not model_fixtures and not evs: issues.append(f'{key}: missing event stream')
        if evs:
            if not all(type(e.get('Ordinal')) is int and _number(e.get('TimeMs')) and isinstance(e.get('Type'),str) for e in evs):
                issues.append(f'{key}: malformed event stream')
                continue
            ords = [e['Ordinal'] for e in evs]
            if ords != list(range(len(ords))): issues.append(f'{key}: missing/duplicate/reordered event')
            requests = [e for e in evs if e['Type']=='RewardRequested']
            if len(requests)>1: issues.append(f'{key}: duplicate reward request')
            if amount == 0 and requests: issues.append(f'{key}: zero has a reward request event')
            if r['Status']=='Completed' and amount and len(requests)!=1: issues.append(f'{key}: missing reward request event')
            if any(b['TimeMs'] < a['TimeMs'] for a,b in zip(evs,evs[1:])): issues.append(f'{key}: nonmonotonic events')
            if any(e['TimeMs']<r['OnsetMs'] or e['TimeMs']>r['EndMs'] for e in evs): issues.append(f'{key}: event outside trial interval')
            required = ['OfferVisibleSoftware','TrialFinalized']
            if r['Status']=='Completed': required += ['ChoiceCommitted','OutcomeResolved','RevealVisibleSoftware']
            for event_type in required:
                if sum(e['Type']==event_type for e in evs)!=1: issues.append(f'{key}: missing/duplicate {event_type}')
            if evs[-1]['Type']!='TrialFinalized' or evs[-1]['TimeMs']!=r['EndMs']: issues.append(f'{key}: final event/record mismatch')
        trials.append(dict(trial_id=key, trial_index=r['TrialIndex'], status=r['Status'], reason=r.get('Reason'),
                           choice_count=len(offered), chosen_id=r.get('ChosenId'), chosen_position=chosen,
                           role=r.get('Role'), tokens=amount, draw=r.get('Draw'), contact_rt_ms=r.get('FirstContactRTMs'),
                           commit_rt_ms=r.get('CommitRTMs'), requested_pulses=r['RequestedPulses'], observed_pulses=r.get('ObservedPulses'),
                           reward_status=r['RewardStatus'], renderer=c['Renderer'], palette_mode=c['PaletteMode'],
                           input_source=r.get('InputSource','Unknown'),
                           iti_ms=c['ItiMs'], reveal_late_ms=r['RevealLateMs'], duration_ms=r['EndMs']-r['OnsetMs']))
    incomplete = sorted((set(prepared)|set(events)|set(checkpoints))-set(rows))
    for key in incomplete: issues.append(f'{key}: incomplete trial, no final record')
    output.mkdir(parents=True, exist_ok=True)
    empty_columns={'trials':'trial_id trial_index status reason choice_count chosen_id chosen_position role tokens draw contact_rt_ms commit_rt_ms requested_pulses observed_pulses reward_status renderer palette_mode input_source iti_ms reveal_late_ms duration_ms',
                   'options':'trial_id position option_id low high p_high ev variance palette chosen'}
    event_rows=[]
    for key, stream in events.items():
        for event in stream:
            if 'trial_id' in event:
                issues.append(f'{key}: event contains reserved export field trial_id')
            event_rows.append({**event, 'trial_id': key})
    for name, data in [('trials',trials), ('options',options), ('outcomes',outcomes), ('ledger',ledger_rows), ('events',event_rows)]:
        with (output/(name+'.csv')).open('w',newline='',encoding='utf-8') as f:
            columns=list(dict.fromkeys(k for row in data for k in row)) if data else empty_columns.get(name,'trial_id').split()
            writer=csv.DictWriter(f,fieldnames=columns);writer.writeheader();writer.writerows(data)
    summary=dict(source=str(path.resolve()), trials=len(trials), options=len(options), incomplete=incomplete,
                 statuses={s:sum(t['status']==s for t in trials) for s in sorted({t['status'] for t in trials})},
                 completed_tokens=sum(t['tokens'] or 0 for t in trials if t['status']=='Completed'),
                 practice_completed_tokens=sum(t['tokens'] or 0 for t in trials if t['status']=='Completed' and t.get('practice',False)),
                 nonpractice_completed_tokens=sum(t['tokens'] or 0 for t in trials if t['status']=='Completed' and not t.get('practice',False)),
                 requested_pulses=sum(t['requested_pulses'] for t in trials),issues=issues,
                 passed=not issues, evidence='Model fixtures' if model_fixtures or trials and all('ModelFixture' in t.get('input_source','') for t in trials) else 'Runtime journal', note='Requested/queued/simulated pulses are not measured juice delivery.')
    (output/'validation.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    return summary


def _validate_record(r):
    """Reject malformed input with a report instead of crashing or accepting NaN."""
    for name in ('SchemaVersion','TrialIndex','ChosenIndex','RequestedPulses'):
        if type(r[name]) is not int: raise ValueError(f'{name} must be an integer')
    if r['TrialIndex']<0 or r['RequestedPulses']<0: raise ValueError('negative trial index/pulse count')
    if r.get('Tokens') is not None and (type(r['Tokens']) is not int or not 0<=r['Tokens']<=6): raise ValueError('invalid tokens')
    for name in ('Status','RewardStatus','RandomAlgorithm'):
        if not isinstance(r[name],str): raise ValueError(f'{name} must be text')
    if 'InputSource' in r and (not isinstance(r['InputSource'],str) or not r['InputSource']):
        raise ValueError('InputSource must be nonempty text')
    c = r['Config']
    validate_visual_config(c,1)
    if r.get('ObservedPulses') is not None and (type(r['ObservedPulses']) is not int or r['ObservedPulses']<0):
        raise ValueError('invalid observed pulse count')
    for name in ('SchemaVersion','Seed','ChoiceCount','PulsesPerToken'):
        if type(c[name]) is not int: raise ValueError(f'Config.{name} must be an integer')
    if not isinstance(c['PhysicalReward'],bool): raise ValueError('PhysicalReward must be Boolean')
    for name in ('HoldMs','DeadlineMs','RevealMs','HighlightMs','TransferMs','RewardWindowMs','ItiMs'):
        if not _number(c[name]) or c[name]<0: raise ValueError(f'invalid {name}')
    if not 0<c['HoldMs']<=c['DeadlineMs']<=c['RevealMs']: raise ValueError('invalid timing ordering')
    for name in ('OnsetMs','EndMs','RevealActualMs','TransferEndMs','ItiStartMs','RevealLateMs'):
        if not _number(r[name]): raise ValueError(f'invalid {name}')
    for name in ('Draw','CommitRTMs','FirstContactRTMs'):
        v=r.get(name)
        if v is not None and not _number(v): raise ValueError(f'invalid {name}')
    if r['Status']=='Completed' and any(r.get(n) is None for n in ('Draw','CommitRTMs','Tokens')): raise ValueError('completed trial missing outcome/timing')
    if not isinstance(r['Options'],list) or not 2<=len(r['Options'])<=4: raise ValueError('invalid options array')
    for o in r['Options']:
        if not isinstance(o,dict) or not isinstance(o['Id'],str) or not o['Id'].strip(): raise ValueError('invalid option')
        if any(type(o[n]) is not int for n in ('Low','High','Palette')) or not _number(o['PHigh']): raise ValueError('invalid option number')
        if o['Palette'] not in (0,1): raise ValueError('invalid palette index')
    for name in ('TrialId','Renderer','PaletteMode','ItiMs'):
        (r if name=='TrialId' else c)[name]

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('journal',type=Path);parser.add_argument('--output',type=Path)
    parser.add_argument('--model-fixtures',action='store_true',help='Model-only records intentionally have no runtime event stream')
    args=parser.parse_args();report=analyze(args.journal,args.output or args.journal.parent/'analysis',args.model_fixtures)
    print(json.dumps(report,indent=2));raise SystemExit(0 if report['passed'] else 1)
