"""GambleChoice module manager. Python 3.9+, standard library only; also frozen as an EXE.

Run without arguments for the desktop manager. Mutations require a closed Unity editor.
All installation changes are journaled before writing; shared files use owned sections.
"""
import argparse
import base64
import contextlib
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import time
import uuid

ROOT = Path(getattr(sys, '_MEIPASS', Path(__file__).resolve().parent))
REGISTRY = 'Assets/_USE_Tasks/USE_Tasks_CustomTypes.cs'
BUILD = 'ProjectSettings/EditorBuildSettings.asset'
SCENE = 'Assets/_USE_Tasks/GambleChoice/GambleChoice.unity'
STATE = 'UserSettings/GambleChoiceModule'
USING = '// GambleChoice module: using\nusing GambleChoice_Namespace;\n// GambleChoice module: end using\n'
ENTRY = '''        // GambleChoice module: registration
        { "GambleChoice", new TaskTypes { TaskLevelType=typeof(GambleChoice_TaskLevel), TrialLevelType=typeof(GambleChoice_TrialLevel), TaskDefType=typeof(GambleChoice_TaskDef), BlockDefType=typeof(GambleChoice_BlockDef), TrialDefType=typeof(GambleChoice_TrialDef), StimDefType=typeof(GambleChoice_StimDef) } },
        // GambleChoice module: end registration
'''


class Conflict(RuntimeError):
    pass


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe(root, relative):
    """Reject traversal and symlinks/junctions escaping the selected Unity project."""
    rel = PurePosixPath(relative)
    if rel.is_absolute() or '\\' in relative or ':' in relative or '..' in rel.parts:
        raise Conflict('Unsafe module path: ' + relative)
    path = root.joinpath(*rel.parts)
    if root.resolve() not in path.resolve().parents:
        raise Conflict('Path escapes project: ' + relative)
    return path


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, data):
    atomic(path, (json.dumps(data, indent=2) + '\n').encode())


def atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.gamble-' + uuid.uuid4().hex)
    try:
        temp.write_bytes(data)
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def project_path(path):
    root = Path(path).resolve()
    if not (root / 'ProjectSettings/ProjectVersion.txt').is_file():
        if (root / 'USE_CORE/ProjectSettings/ProjectVersion.txt').is_file():
            root /= 'USE_CORE'
        else:
            raise Conflict('Select the Unity project folder containing Assets, Packages and ProjectSettings.')
    return root


def text_patch(data, add, anchor):
    newline = '\r\n' if b'\r\n' in data else '\n'
    text = data.decode('utf-8-sig')
    if text.count(anchor) != 1:
        raise Conflict('Expected exactly one integration anchor: ' + anchor)
    # Preserve BOM and original newline style.
    result = text.replace(anchor, add.replace('\n', newline) + anchor, 1).encode('utf-8')
    return (b'\xef\xbb\xbf' if data.startswith(b'\xef\xbb\xbf') else b'') + result


def patch_registry(data):
    if b'GambleChoice' in data:
        raise Conflict('GambleChoice already appears in the registry. Existing manual installations must be reviewed; no files were adopted.')
    result = text_patch(data, USING, '//#END OF USING DIRECTIVES - DO NOT DELETE THIS LINE#')
    text = result.decode('utf-8-sig')
    matches = list(re.finditer(r'CustomTaskDictionary\s*=\s*new Dictionary<string,\s*TaskTypes>\s*\(\s*\)\s*\{', text))
    if len(matches) != 1:
        raise Conflict('Unsupported task registry structure.')
    offset = matches[0].end()
    nl = '\r\n' if b'\r\n' in result else '\n'
    addition = nl + ENTRY.replace('\n', nl).rstrip('\r\n')
    text = text[:offset] + addition + text[offset:]
    return (b'\xef\xbb\xbf' if data.startswith(b'\xef\xbb\xbf') else b'') + text.encode()


def check_closed(project):
    lock = project / 'Temp/UnityLockfile'
    if not lock.exists():
        return
    if os.name == 'nt':
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                      wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel.CreateFileW.restype = wintypes.HANDLE
        handle = kernel.CreateFileW(str(lock), 0x80000000, 0, None, 3, 0, None)
        if handle == ctypes.c_void_p(-1).value:
            raise Conflict('Close Unity for this project before installing, validating or disconnecting.')
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.CloseHandle(handle)
    else:
        import fcntl
        with lock.open('rb') as stream:
            try:
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError as error:
                raise Conflict('Close Unity for this project first.') from error


@contextlib.contextmanager
def operation(project):
    folder = safe(project, STATE)
    folder.mkdir(parents=True, exist_ok=True)
    lock = folder / 'operation.lock'
    try:
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError as error:
        raise Conflict('Another operation is active, or was interrupted. Check running managers before removing ' + str(lock)) from error
    os.close(fd)
    try:
        check_closed(project)
        yield
    finally:
        lock.unlink()


def manifest(root=ROOT):
    info = load(root / 'module.json')
    for relative, expected in info['files'].items():
        path = safe(root / 'payload', relative)
        if not path.is_file() or digest(path.read_bytes()) != expected:
            raise Conflict('Release payload is missing or modified: ' + relative)
    return info


def compatibility(project):
    version = (project / 'ProjectSettings/ProjectVersion.txt').read_text()
    if 'm_EditorVersion: 2020.3.40f1\n' not in version.replace('\r\n', '\n'):
        raise Conflict('This release targets Unity 2020.3.40f1. Other versions require a tested adapter.')
    deps = load(project / 'Packages/manifest.json')['dependencies']
    for package in ['com.unity.nuget.newtonsoft-json', 'com.unity.ugui']:
        if package not in deps:
            raise Conflict('Required existing package is missing: ' + package)
    scripts = project / 'Assets/_Scripts/M_USE'
    requirements = {'InputBroker.cs': ['isSimulation', 'GetMouseButtonDown', 'GetMouseButton'],
                    'USE_ExperimentTemplate_Task.cs': ['TaskCanvasses', 'ForceTaskEnd'],
                    'USE_ExperimentTemplate_Trial.cs': ['FinishTrialCleanup', 'ResetTrialVariables'],
                    'USE_ExperimentTemplate_Session.cs': ['TasksFinished'],
                    'Session.cs': ['StoreData', 'StoringDataOnServer']}
    for filename, members in requirements.items():
        matches = list(scripts.rglob(filename))
        if len(matches) != 1:
            raise Conflict('Missing or ambiguous framework source: ' + filename)
        text = matches[0].read_text(encoding='utf-8-sig')
        for member in members:
            if member not in text:
                raise Conflict('Unsupported framework API: ' + filename + ' / ' + member)
    return 'Capability preflight passed; Unity compilation and checks are required.'


def record_path(project):
    return safe(project, STATE + '/installation.json')


def make_change(project, relative, after, kind):
    path = safe(project, relative)
    before = path.read_bytes() if path.is_file() else None
    return {'path': relative, 'kind': kind,
            'before': base64.b64encode(before).decode() if before is not None else None,
            'after': base64.b64encode(after).decode() if after is not None else None}


def remove_sections(current, installed, original, relative):
    """Remove only our exact snippets; preserve unrelated edits to shared files."""
    if current == installed:
        return original
    if relative == REGISTRY:
        result = current
        for snippet in [USING, '\n' + ENTRY.rstrip('\n')]:
            snippet = snippet.replace('\n', '\r\n' if b'\r\n' in installed else '\n').encode()
            if result.count(snippet) != 1:
                raise Conflict('The owned registry section was edited. Review it before disconnecting.')
            result = result.replace(snippet, b'', 1)
        if b'GambleChoice' in result:
            raise Conflict('Additional GambleChoice registry references need manual review.')
        return result
    if relative == BUILD:
        # The exact scene entry is recorded from the installation, including its GUID.
        match = re.search(rb'  - enabled: 1\r?\n    path: ' + re.escape(SCENE.encode()) + rb'\r?\n    guid: [0-9a-f]+\r?\n', installed)
        addition = match[0] if match else None
        if not addition or current.count(addition) != 1:
            raise Conflict('The installed build scene entry was edited; review before disconnecting.')
        return current.replace(addition, b'', 1)
    raise Conflict('A shared framework patch was subsequently edited: ' + relative)


def plan(project, module=ROOT):
    info = manifest(module)
    compatibility(project)
    path = record_path(project)
    if path.exists():
        record = load(path)
        if record['status'] != 'connected':
            raise Conflict('An interrupted installation needs recovery before continuing.')
        old = {c['path']: c for c in record['changes']}
        changes = []
        for relative, expected in info['files'].items():
            if relative not in old:
                if safe(project, relative).exists():
                    raise Conflict('New release conflicts with existing file: ' + relative)
            else:
                prior = base64.b64decode(old[relative]['after'])
                if not safe(project, relative).is_file() or safe(project, relative).read_bytes() != prior:
                    raise Conflict('Installed file has local edits or is missing: ' + relative)
            changes.append(make_change(project, relative, safe(module / 'payload', relative).read_bytes(), 'payload'))
        for relative, change in old.items():
            if change['kind'] == 'payload' and relative not in info['files']:
                current = safe(project, relative)
                if not current.is_file() or current.read_bytes() != base64.b64decode(change['after']):
                    raise Conflict('Cannot remove modified obsolete file: ' + relative)
                changes.append(make_change(project, relative, None, 'obsolete'))
            if change['kind'] == 'shared':
                current = safe(project, relative).read_bytes()
                remove_sections(current, base64.b64decode(change['after']), base64.b64decode(change['before']), relative)
        return info, changes, record
    changes = []
    # GUID collisions can silently break unrelated scenes even when filenames differ.
    incoming = {}
    for relative in info['files']:
        if safe(project, relative).exists():
            raise Conflict('Existing file would be overwritten: ' + relative)
        data = safe(module / 'payload', relative).read_bytes()
        if relative.endswith('.meta'):
            match = re.search(rb'(?m)^guid: ([0-9a-f]+)', data)
            if match:
                if match[1] in incoming:
                    raise Conflict('Duplicate GUID inside the release: ' + relative)
                incoming[match[1]] = relative
        changes.append(make_change(project, relative, data, 'payload'))
    for existing in (project / 'Assets').rglob('*.meta'):
        match = re.search(rb'(?m)^guid: ([0-9a-f]+)', existing.read_bytes())
        if match and match[1] in incoming:
            raise Conflict('Unity GUID collision: ' + incoming[match[1]] + ' / ' + str(existing))
    data = safe(project, REGISTRY).read_bytes()
    changes.append(make_change(project, REGISTRY, patch_registry(data), 'shared'))
    data = safe(project, BUILD).read_bytes()
    if SCENE.encode() in data:
        raise Conflict('GambleChoice is already present in Build Settings.')
    if b'm_Scenes:' not in data:
        raise Conflict('Unsupported Build Settings structure.')
    if b'm_Scenes: []' in data:
        raise Conflict('Empty build lists need an adapter; no changes were made.')
    guid = re.search(rb'guid: ([0-9a-f]+)', safe(module / 'payload', SCENE + '.meta').read_bytes())[1].decode()
    # Insert before m_configObjects (not after it): Unity serializes additional keys after m_Scenes.
    nl = '\r\n' if b'\r\n' in data else '\n'
    addition = ('  - enabled: 1\n    path: ' + SCENE + '\n    guid: ' + guid + '\n').replace('\n', nl).encode()
    if b'  m_configObjects:' in data:
        patched = data.replace(b'  m_configObjects:', addition + b'  m_configObjects:', 1)
    else:
        patched = data + addition
    changes.append(make_change(project, BUILD, patched, 'shared'))
    return info, changes, None


def apply_changes(project, changes, field):
    for change in changes:
        path = safe(project, change['path'])
        data = change[field]
        if data is None:
            path.unlink(missing_ok=True)
        else:
            atomic(path, base64.b64decode(data))


def validate(project, unity):
    if not unity or not Path(unity).is_file():
        raise Conflict('Choose the Unity 2020.3.40f1 editor executable; validation cannot be skipped.')
    # Verify the executable before allowing it to open (and possibly upgrade) the project.
    if os.name == 'nt':
        import ctypes
        from ctypes import wintypes
        dll = ctypes.WinDLL('version', use_last_error=True)
        dll.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
        dll.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
        dll.VerQueryValueW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]
        ignored = wintypes.DWORD()
        size = dll.GetFileVersionInfoSizeW(str(unity), ctypes.byref(ignored))
        buffer = ctypes.create_string_buffer(size)
        pointer, length = ctypes.c_void_p(), wintypes.UINT()
        if not size or not dll.GetFileVersionInfoW(str(unity), 0, size, buffer):
            raise Conflict('Cannot verify the Unity editor version.')
        if not dll.VerQueryValueW(buffer, '\\VarFileInfo\\Translation', ctypes.byref(pointer), ctypes.byref(length)):
            raise Conflict('Cannot read the Unity editor version.')
        translation = ctypes.cast(pointer, ctypes.POINTER(ctypes.c_ushort))
        key = '\\StringFileInfo\\%04x%04x\\ProductVersion' % (translation[0], translation[1])
        if not dll.VerQueryValueW(buffer, key, ctypes.byref(pointer), ctypes.byref(length)):
            raise Conflict('Cannot read the Unity product version.')
        product = ctypes.wstring_at(pointer)
        if not product.startswith('2020.3.40f1_') and product != '2020.3.40f1':
            raise Conflict('Selected editor is ' + product + '. Choose Unity 2020.3.40f1.')
    check_closed(project)
    folder = safe(project, STATE)
    folder.mkdir(parents=True, exist_ok=True)
    report = folder / 'checks.json'
    report.unlink(missing_ok=True)
    token = uuid.uuid4().hex
    env = dict(os.environ, GAMBLE_VALIDATION_ID=token)
    command = [str(unity), '-batchmode', '-nographics', '-quit', '-projectPath', str(project),
               '-executeMethod', 'GambleChoiceModuleChecks.Run', '-logFile', str(folder / 'unity.log')]
    result = subprocess.run(command, env=env, timeout=1800, creationflags=0x08000000 if os.name == 'nt' else 0)
    if result.returncode or not report.exists():
        raise Conflict('Unity compilation/checks failed. See ' + str(folder / 'unity.log'))
    evidence = load(report)
    if not evidence.get('passed') or evidence.get('validationId') != token:
        raise Conflict('Unity returned no fresh passing validation report.')
    return evidence


def connect(project, unity, module=ROOT, validator=validate):
    with operation(project):
        info, changes, previous = plan(project, module)
        journal = {'status': 'pending', 'version': info['version'], 'changes': changes, 'previous': previous}
        write_json(record_path(project), journal)
        try:
            apply_changes(project, changes, 'after')
            evidence = validator(project, unity)
        except BaseException:
            recover(project)
            raise
        if previous:
            baseline = {c['path']: c for c in previous['changes']}
            final = [c for c in previous['changes'] if c['kind'] == 'shared']
            for change in changes:
                if change['kind'] != 'payload':
                    continue
                original = baseline.get(change['path'])
                change['before'] = original['before'] if original else None
                final.append(change)
            journal['changes'] = final
        journal.update(status='connected', evidence=evidence, previous=None)
        write_json(record_path(project), journal)
        return {'version': info['version'], 'changed_files': len(changes), 'validation': evidence}


def recover(project):
    """Restore a pending transaction, refusing to overwrite post-interruption edits."""
    path = record_path(project)
    journal = load(path)
    if journal['status'] != 'pending':
        raise Conflict('No pending transaction to recover.')
    for change in journal['changes']:
        target = safe(project, change['path'])
        current = target.read_bytes() if target.is_file() else None
        candidates = [base64.b64decode(change[key]) if change[key] is not None else None for key in ['before', 'after']]
        if current not in candidates:
            raise Conflict('Recovery found a subsequently edited file; backups retained: ' + change['path'])
    apply_changes(project, journal['changes'], 'before')
    if journal.get('previous'):
        write_json(path, journal['previous'])
    else:
        path.unlink()


def disconnect_plan(project):
    record = load(record_path(project))
    if record['status'] != 'connected':
        raise Conflict('Recover the interrupted transaction first.')
    changes = []
    owned = {c['path'] for c in record['changes']}
    guids = set()
    exported_types = {b'GambleChoice'}
    for change in record['changes']:
        path = safe(project, change['path'])
        current = path.read_bytes() if path.is_file() else None
        installed = base64.b64decode(change['after'])
        original = base64.b64decode(change['before']) if change['before'] is not None else None
        if change['kind'] == 'payload':
            if current != installed:
                raise Conflict('Installed file was edited or removed; disconnect stopped: ' + change['path'])
            if change['path'].endswith('.meta'):
                match = re.search(rb'(?m)^guid: ([0-9a-f]+)', installed)
                if match:
                    guids.add(match[1])
            if change['path'].endswith('.cs'):
                exported_types.update(name for name in re.findall(rb'\b(?:class|struct|enum|interface)\s+(\w+)', installed)
                                      if name.startswith(b'Gamble'))
            after = original
        else:
            if current is None:
                raise Conflict('Missing shared file: ' + change['path'])
            after = remove_sections(current, installed, original, change['path'])
        changes.append(make_change(project, change['path'], after, change['kind']))
    references = re.compile(rb'\b(?:' + b'|'.join(re.escape(name) for name in sorted(exported_types)) + rb')\b')
    for path in [p for root in ['Assets', 'Packages'] for p in (project / root).rglob('*')]:
        if not path.is_file() or path.relative_to(project).as_posix() in owned:
            continue
        # Unity references and custom scripts outside the module require explicit review.
        if path.suffix.lower() in {'.unity', '.prefab', '.asset', '.cs', '.json', '.asmdef'}:
            data = path.read_bytes()
            if references.search(data) or any(guid in data for guid in guids):
                raise Conflict('External reference would remain: ' + str(path.relative_to(project)))
    return record, changes


def disconnect(project):
    with operation(project):
        record, changes = disconnect_plan(project)
        write_json(record_path(project), {'status': 'pending', 'changes': changes, 'previous': record})
        try:
            apply_changes(project, changes, 'after')
        except BaseException:
            recover(project)
            raise
        record_path(project).unlink()
        # Remove empty owned asset folders only. Never recursively delete a directory.
        for folder in sorted((project / 'Assets').rglob('*'), key=lambda p: len(p.parts), reverse=True):
            if folder.is_dir() and ('GambleChoice' in folder.parts or 'GambleChoiceTemplates' in folder.parts):
                try:
                    folder.rmdir()
                except OSError:
                    pass
        return {'disconnected': True, 'preserved': 'Exported protocols, recordings, validation logs and unrelated changes.'}


def default_unity():
    path = Path('C:/Program Files/Unity/Hub/Editor/2020.3.40f1/Editor/Unity.exe')
    return str(path) if path.exists() else ''


def gui():
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    import threading
    app = tk.Tk()
    app.title('GambleChoice — Connect / Disconnect')
    app.geometry('850x600')
    project = tk.StringVar()
    unity = tk.StringVar(value=default_unity())
    ttk.Label(app, text='Add GambleChoice to your existing M-USE project', font=('', 15)).pack(pady=12)
    ttk.Label(app, text='Close that project in Unity first. Preview checks compatibility without changing project files.').pack()
    for title, value, browse in [('M-USE Unity project', project, filedialog.askdirectory),
                                  ('Unity 2020.3.40f1 executable', unity, filedialog.askopenfilename)]:
        row = ttk.Frame(app)
        row.pack(fill='x', padx=15, pady=6)
        ttk.Label(row, text=title, width=28).pack(side='left')
        ttk.Entry(row, textvariable=value).pack(side='left', fill='x', expand=True)
        ttk.Button(row, text='Browse', command=lambda v=value, b=browse: v.set(b() or v.get())).pack(side='right')
    output = tk.Text(app, wrap='word')
    output.pack(fill='both', expand=True, padx=15, pady=10)
    buttons = []

    def work(action):
        try:
            root = project_path(project.get())
            editor = unity.get()
        except Exception as error:
            messagebox.showerror('Choose a project', str(error))
            return
        for button in buttons:
            button.config(state='disabled')
        output.delete('1.0', 'end')
        output.insert('end', 'Working… Unity validation may take several minutes.\n')

        def finish(message):
            output.delete('1.0', 'end')
            output.insert('end', message)
            for button in buttons:
                button.config(state='normal')

        def run():
            try:
                if action == 'preview':
                    info, changes, old = plan(root)
                    result = {'version': info['version'], 'action': 'update' if old else 'connect',
                              'files': [c['path'] for c in changes],
                              'next': 'Connect runs Unity compilation and checks, with rollback on failure.'}
                elif action == 'connect':
                    result = connect(root, editor)
                elif action == 'validate':
                    with operation(root):
                        result = validate(root, editor)
                elif action == 'disconnect':
                    result = disconnect(root)
                else:
                    with operation(root):
                        recover(root)
                    result = {'recovered': True}
                message = json.dumps(result, indent=2)
            except Exception as error:
                message = 'Stopped: ' + str(error)
            app.after(0, lambda: finish(message))
        threading.Thread(target=run, daemon=False).start()

    bar = ttk.Frame(app)
    bar.pack(pady=10)
    for label, action in [('Preview', 'preview'), ('Connect / Update', 'connect'), ('Validate', 'validate'),
                          ('Disconnect', 'disconnect'), ('Recover', 'recover')]:
        button = ttk.Button(bar, text=label, command=lambda a=action: work(a))
        button.pack(side='left', padx=5)
        buttons.append(button)
    app.mainloop()


def main():
    if len(sys.argv) == 1:
        gui()
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['preview', 'connect', 'disconnect', 'validate', 'recover'])
    parser.add_argument('project')
    parser.add_argument('--unity', default=default_unity())
    args = parser.parse_args()
    root = project_path(args.project)
    if args.action == 'preview':
        info, changes, previous = plan(root)
        result = {'version': info['version'], 'update': previous is not None, 'files': [c['path'] for c in changes]}
    elif args.action == 'connect':
        result = connect(root, args.unity)
    elif args.action == 'disconnect':
        result = disconnect(root)
    elif args.action == 'validate':
        with operation(root):
            result = validate(root, args.unity)
    else:
        with operation(root):
            recover(root)
        result = {'recovered': True}
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('Stopped: ' + str(error), file=sys.stderr)
        sys.exit(1)
