import base64
import importlib.util
import json
from pathlib import Path
import shutil
import os
import sys
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('manager', Path(__file__).resolve().parents[1] / 'manager.py')
m = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)


class ManagerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / 'a differently named fork/Unity Project'
        self.module = self.root / 'module'
        self.write('ProjectSettings/ProjectVersion.txt', 'm_EditorVersion: 2020.3.40f1\n')
        self.write('Packages/manifest.json', json.dumps({'dependencies': {
            'com.unity.nuget.newtonsoft-json': '3.2.1', 'com.unity.ugui': '1.0.0'}}))
        for name, words in {'InputBroker.cs': 'isSimulation GetMouseButtonDown GetMouseButton',
                            'USE_ExperimentTemplate_Task.cs': 'TaskCanvasses ForceTaskEnd',
                            'USE_ExperimentTemplate_Trial.cs': 'FinishTrialCleanup ResetTrialVariables',
                            'USE_ExperimentTemplate_Session.cs': 'TasksFinished',
                            'Session.cs': 'StoreData StoringDataOnServer'}.items():
            self.write('Assets/_Scripts/M_USE/' + name, words)
        registry = '''using System;
//#END OF USING DIRECTIVES - DO NOT DELETE THIS LINE#
static public class USE_Tasks_CustomTypes {
static public Dictionary<string, TaskTypes> CustomTaskDictionary = new Dictionary<string, TaskTypes>()
    {
        { "OtherTask", new TaskTypes() },
    };
}
'''
        self.write(m.REGISTRY, registry)
        self.write(m.BUILD, 'EditorBuildSettings:\n  m_Scenes:\n  - enabled: 1\n    path: Assets/Other.unity\n    guid: abc\n  m_configObjects: {}\n')
        self.before = {rel: (self.project / rel).read_bytes() for rel in [m.REGISTRY, m.BUILD]}
        self.files = {m.SCENE: b'example scene', m.SCENE + '.meta': b'fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n',
                      'Assets/_USE_Tasks/GambleChoice/Example.cs': b'example source'}
        self.refresh()

    def write(self, relative, text):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.encode())

    def refresh(self, version='0.1.0'):
        for relative, data in self.files.items():
            path = self.module / 'payload' / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        m.write_json(self.module / 'module.json', {'version': version, 'files': {k: m.digest(v) for k, v in self.files.items()}})

    def connect(self, validator=lambda *args: {'passed': True, 'kind': 'test double'}):
        return m.connect(self.project, None, self.module, validator)

    def assert_original(self):
        for relative, data in self.before.items():
            self.assertEqual(data, (self.project / relative).read_bytes())
        for relative in self.files:
            self.assertFalse((self.project / relative).exists())

    def test_preview_is_read_only(self):
        _, changes, previous = m.plan(self.project, self.module)
        self.assertIsNone(previous)
        self.assertEqual(len(changes), 5)
        self.assertFalse(m.record_path(self.project).exists())
        self.assert_original()

    def test_roundtrip_preserves_data_and_protocols(self):
        self.write('GambleChoiceConfigs/edited.json', 'my protocol')
        self.write('Data/participant.txt', 'never touch')
        self.connect()
        m.disconnect(self.project)
        self.assert_original()
        self.assertEqual((self.project / 'Data/participant.txt').read_text(), 'never touch')
        self.assertEqual((self.project / 'GambleChoiceConfigs/edited.json').read_text(), 'my protocol')

    def test_failed_validation_rolls_back(self):
        def fail(*args):
            raise m.Conflict('deliberate compilation failure')
        with self.assertRaisesRegex(m.Conflict, 'deliberate'):
            self.connect(fail)
        self.assert_original()
        self.assertFalse(m.record_path(self.project).exists())

    def test_edited_owned_file_blocks_disconnect(self):
        self.connect()
        (self.project / m.SCENE).write_text('operator edit')
        with self.assertRaisesRegex(m.Conflict, 'edited'):
            m.disconnect(self.project)
        self.assertTrue(m.record_path(self.project).exists())
        self.assertIn(b'GambleChoice', (self.project / m.REGISTRY).read_bytes())

    def test_edited_owned_file_blocks_update(self):
        self.connect()
        (self.project / m.SCENE).write_text('operator edit')
        with self.assertRaisesRegex(m.Conflict, 'local edits'):
            self.connect()

    def test_shared_unrelated_edits_survive(self):
        self.connect()
        with (self.project / m.REGISTRY).open('ab') as stream:
            stream.write(b'// another lab change\n')
        with (self.project / m.BUILD).open('ab') as stream:
            stream.write(b'  anotherSetting: 1\n')
        m.disconnect(self.project)
        self.assertEqual((self.project / m.REGISTRY).read_bytes(), self.before[m.REGISTRY] + b'// another lab change\n')
        self.assertEqual((self.project / m.BUILD).read_bytes(), self.before[m.BUILD] + b'  anotherSetting: 1\n')

    def test_shared_owned_edit_blocks(self):
        self.connect()
        path = self.project / m.REGISTRY
        path.write_bytes(path.read_bytes().replace(b'GambleChoice_TaskLevel', b'Custom_TaskLevel'))
        with self.assertRaisesRegex(m.Conflict, 'registry section'):
            m.disconnect(self.project)

    def test_external_scene_reference_blocks(self):
        self.connect()
        self.write('Assets/AnotherLab.prefab', 'guid: 11111111111111111111111111111111')
        with self.assertRaisesRegex(m.Conflict, 'External reference'):
            m.disconnect(self.project)

    def test_external_script_reference_blocks(self):
        self.connect()
        self.write('Assets/AnotherLab.cs', 'using GambleChoice;')
        with self.assertRaisesRegex(m.Conflict, 'External reference'):
            m.disconnect(self.project)

    def test_global_editor_api_reference_blocks(self):
        self.files['Assets/_USE_Tasks/GambleChoice/Example.cs'] = b'public class GambleCompiler {}'
        self.refresh()
        self.connect()
        self.write('Assets/Consumer.cs', 'GambleCompiler.Export();')
        with self.assertRaisesRegex(m.Conflict, 'External reference'):
            m.disconnect(self.project)

    def test_embedded_package_reference_blocks(self):
        self.connect()
        self.write('Packages/lab-tool/Consumer.cs', 'using GambleChoice;')
        with self.assertRaisesRegex(m.Conflict, 'External reference'):
            m.disconnect(self.project)

    def test_existing_manual_install_refused(self):
        self.write(m.SCENE, 'manual scene')
        with self.assertRaisesRegex(m.Conflict, 'overwritten'):
            self.connect()

    def test_existing_registry_entry_refused(self):
        self.write(m.REGISTRY, 'using GambleChoice_Namespace;')
        with self.assertRaisesRegex(m.Conflict, 'already appears'):
            self.connect()

    def test_guid_collision_refused(self):
        self.write('Assets/Unrelated.cs.meta', 'guid: 11111111111111111111111111111111\n')
        with self.assertRaisesRegex(m.Conflict, 'GUID collision'):
            self.connect()

    def test_corrupt_release_refused(self):
        (self.module / 'payload' / m.SCENE).write_text('corrupt release')
        with self.assertRaisesRegex(m.Conflict, 'modified'):
            self.connect()
        self.assert_original()

    def test_duplicate_payload_guid_refused(self):
        self.files['Assets/_USE_Tasks/GambleChoice/Example.cs.meta'] = self.files[m.SCENE + '.meta']
        self.refresh()
        with self.assertRaisesRegex(m.Conflict, 'Duplicate GUID'):
            self.connect()

    def test_unsupported_unity_refused(self):
        self.write('ProjectSettings/ProjectVersion.txt', 'm_EditorVersion: 6000.3.0f1\n')
        with self.assertRaisesRegex(m.Conflict, 'Unity 2020'):
            self.connect()

    def test_missing_framework_api_refused(self):
        self.write('Assets/_Scripts/M_USE/Session.cs', 'old fork')
        with self.assertRaisesRegex(m.Conflict, 'Unsupported framework API'):
            self.connect()

    def test_missing_package_refused(self):
        self.write('Packages/manifest.json', '{"dependencies": {}}')
        with self.assertRaisesRegex(m.Conflict, 'package is missing'):
            self.connect()

    def test_newline_and_bom_roundtrip(self):
        for relative in [m.REGISTRY, m.BUILD]:
            data = b'\xef\xbb\xbf' + self.before[relative].replace(b'\n', b'\r\n')
            (self.project / relative).write_bytes(data)
            self.before[relative] = data
        self.connect()
        m.disconnect(self.project)
        self.assert_original()

    def test_update_then_disconnect_restores_original(self):
        self.connect()
        self.files[m.SCENE] = b'updated scene'
        self.files['Assets/_USE_Tasks/GambleChoice/New.cs'] = b'new code'
        self.refresh('0.2.0')
        self.connect()
        self.assertEqual((self.project / m.SCENE).read_bytes(), b'updated scene')
        m.disconnect(self.project)
        self.assert_original()

    def test_failed_update_restores_previous_install(self):
        self.connect()
        record = m.record_path(self.project).read_bytes()
        self.files[m.SCENE] = b'updated scene'
        self.refresh('0.2.0')
        def fail(*args):
            raise m.Conflict('deliberate')
        with self.assertRaises(m.Conflict):
            self.connect(fail)
        self.assertEqual(m.record_path(self.project).read_bytes(), record)
        self.assertEqual((self.project / m.SCENE).read_bytes(), b'example scene')

    def test_obsolete_files_removed_on_update(self):
        self.connect()
        obsolete = 'Assets/_USE_Tasks/GambleChoice/Example.cs'
        del self.files[obsolete]
        self.refresh('0.2.0')
        self.connect()
        self.assertFalse((self.project / obsolete).exists())
        m.disconnect(self.project)
        self.assert_original()

    def test_interrupted_transaction_recovers(self):
        _, changes, _ = m.plan(self.project, self.module)
        m.write_json(m.record_path(self.project), {'status': 'pending', 'changes': changes, 'previous': None})
        m.apply_changes(self.project, changes[:2], 'after')
        m.recover(self.project)
        self.assert_original()

    def test_recovery_preserves_post_interruption_edit(self):
        _, changes, _ = m.plan(self.project, self.module)
        m.write_json(m.record_path(self.project), {'status': 'pending', 'changes': changes, 'previous': None})
        m.apply_changes(self.project, changes[:1], 'after')
        (self.project / m.SCENE).write_text('later edit')
        with self.assertRaisesRegex(m.Conflict, 'subsequently edited'):
            m.recover(self.project)
        self.assertEqual((self.project / m.SCENE).read_text(), 'later edit')

    def test_duplicate_operation_refused(self):
        with m.operation(self.project):
            with self.assertRaisesRegex(m.Conflict, 'Another operation'):
                with m.operation(self.project):
                    pass

    def test_traversal_rejected(self):
        for relative in ['../outside', '/absolute', 'C:/absolute', 'Assets/../../outside', 'Assets\\outside']:
            with self.assertRaises(m.Conflict):
                m.safe(self.project, relative)

    def test_invalid_project_rejected(self):
        with self.assertRaisesRegex(m.Conflict, 'Select the Unity'):
            m.project_path(self.root)

    def test_missing_editor_refused(self):
        with self.assertRaisesRegex(m.Conflict, 'Choose the Unity'):
            m.validate(self.project, str(self.root / 'not-an-editor.exe'))

    @unittest.skipUnless(os.name == 'nt', 'Windows editor metadata')
    def test_wrong_editor_refused_before_launch(self):
        with self.assertRaisesRegex(m.Conflict, 'Choose Unity|Cannot read'):
            m.validate(self.project, sys.executable)

    @unittest.skipUnless(os.name == 'nt', 'Windows Unity lock semantics')
    def test_open_editor_lock_refused(self):
        self.write('Temp/UnityLockfile', 'locked')
        with (self.project / 'Temp/UnityLockfile').open('rb'):
            with self.assertRaisesRegex(m.Conflict, 'Close Unity'):
                m.check_closed(self.project)

    def test_stale_editor_lock_allows_operation(self):
        self.write('Temp/UnityLockfile', 'stale')
        m.check_closed(self.project)

    def test_repo_root_resolves_unity_subdirectory(self):
        self.assertEqual(m.project_path(self.project), self.project)
        self.project.rename(self.project.parent / 'USE_CORE')
        self.assertEqual(m.project_path(self.project.parent), self.project.parent / 'USE_CORE')


if __name__ == '__main__':
    unittest.main()
