"""Live hook regression using only the disposable CLICI-PROOF-input fixture.

Run the helper's `anchor-fixture <fixture-directory>` command in that portable
Terminal window first. This tests marked injected input through the real Windows
hook; it is not a physical-keyboard or native-agent qualification claim.
`--frames` drives the helper through the frame file Claude Code's plugin writes
(`renderer-bridge-frames`) and binds lowercase nonces, the case that plugin
painted before PR #35, so the run goes through RenderedPromptMap.Resolve.
"""
import argparse, ctypes, json, os, pathlib, subprocess, time, threading
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--probe', type=pathlib.Path, required=True)
parser.add_argument('--fixture-directory', type=pathlib.Path, required=True)
parser.add_argument('--frames', action='store_true', help='use the Claude Code frame-file transport')
args = parser.parse_args()
run = args.fixture_directory.resolve()
probe = args.probe.resolve(strict=True)
def call(*args):
    process = subprocess.run([str(probe), *map(str,args)], capture_output=True, text=True, timeout=4)
    if process.returncode: raise RuntimeError(process.stderr or process.stdout)
    return json.loads(process.stdout)
window = None
until = time.monotonic()+15
while time.monotonic()<until:
    window = next((w for w in call('windows') if w['Title'] == 'CLICI-PROOF-input'), None)
    if window and (run/'anchors.json').exists(): break
    time.sleep(.1)
else:
    raise SystemExit('Input fixture did not start; no input sent.')
hwnd = window['Handle']
call('show', hwnd)
ctypes.windll.user32.GetForegroundWindow.restype = ctypes.c_void_p
until = time.monotonic()+60
while ctypes.windll.user32.GetForegroundWindow()!=hwnd and time.monotonic()<until:
    time.sleep(.1)
if ctypes.windll.user32.GetForegroundWindow()!=hwnd:
    raise SystemExit('Awaiting foreground input fixture; no input sent.')
call('live', hwnd)
anchors = json.loads((run / 'anchors.json').read_text())
env = dict(os.environ, CLICI_RENDERER_KEYS='1', CLICI_PROOF_INJECTED_KEYS='1')
env.pop('CLICI_RENDERER_TERMINALS', None)  # Never relax the proof-window guard.
frame_file = run/'frames'/'frame.json'
if args.frames:
    frame_file.parent.mkdir()  # A fresh fixture directory, so this never sweeps a live frame.
    command = [str(probe),'renderer-bridge-frames',str(frame_file)]
else:
    command = [str(probe),'renderer-bridge',str(os.getpid())]
bridge = subprocess.Popen(command, stdin=subprocess.PIPE,
                          stdout=subprocess.PIPE, text=True, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
ctypes.windll.kernel32.GetTickCount64.restype = ctypes.c_ulonglong
sequence = 0
results = []
eligible = True
stop = threading.Event()
def frame(eligible=True):
    global sequence
    sequence += 1
    if args.frames:
        request = dict(version=1,sequence=sequence,observedAt=int(time.time()*1000),agent='claude-code',
            sessionId='CLICI_FIXTURE_INPUT',epoch=1,composerOwnsInput=eligible,historyComplete=True,
            prompts=[dict(messageId=a['messageId'],nonce=a['nonce'].lower()) for a in anchors])
        staged = frame_file.with_suffix('.tmp')
        staged.write_text(json.dumps(request),encoding='utf-8')
        os.replace(staged, frame_file)
    else:
        now = ctypes.windll.kernel32.GetTickCount64()
        request = dict(version=1,sequence=sequence,deadline=now+750,inputObservedAt=now,
            processId=os.getpid(),sessionId='CLICI_FIXTURE_INPUT',epoch=1,direction='observe',
            historyComplete=True,composerOwnsInput=eligible,
            prompts=[dict(messageId=a['messageId'],nonce=a['nonce']) for a in anchors])
        bridge.stdin.write(json.dumps(request)+'\n'); bridge.stdin.flush()
    reply = json.loads(bridge.stdout.readline())
    results.append(reply)
    return reply
def pump():
    while not stop.is_set():
        frame(eligible)
        stop.wait(.1)

class Keyboard(ctypes.Structure):
    _fields_=[('key',ctypes.c_ushort),('scan',ctypes.c_ushort),('flags',ctypes.c_ulong),('time',ctypes.c_ulong),('extra',ctypes.c_size_t)]
class Data(ctypes.Union):
    _fields_=[('keyboard',Keyboard),('padding',ctypes.c_byte*32)]
class Input(ctypes.Structure):
    _fields_=[('kind',ctypes.c_ulong),('data',Data)]
def key_input(key, modifier_order=(0x12, 0x10), release_modifiers_first=False):
    # Only the previously attested disposable fixture, with the test-only marker.
    assert ctypes.windll.user32.GetForegroundWindow()==hwnd
    vk = {'previous': 0xBC, 'next': 0xBE}[key]
    keys = [(m, 0) for m in modifier_order] + [(vk, 0)]
    if release_modifiers_first:
        keys += [(m, 2) for m in reversed(modifier_order)] + [(vk, 0), (vk, 2)]
    else:
        keys += [(vk, 2)] + [(m, 2) for m in reversed(modifier_order)]
    events=(Input*len(keys))(*[Input(1,Data(keyboard=Keyboard(k,0,f,0,0xC11C1))) for k,f in keys])
    assert ctypes.windll.user32.SendInput(len(events),events,ctypes.sizeof(Input))==len(events)

def press(key, **options):
    offset = len(results)
    key_input(key, **options)
    until = time.monotonic()+3
    while time.monotonic()<until:
        replies = [r for r in results[offset:] if r.get('handled')]
        if replies: return replies[0]
        time.sleep(.025)
    raise AssertionError('No confirmed key result: '+key+' '+str(results[offset:]))

worker = None
try:
    results.append(json.loads(bridge.stdout.readline()))
    worker = threading.Thread(target=pump)
    worker.start()
    time.sleep(1)
    for key,action,identity in [('previous','Jump',anchors[2]['messageId']),
        ('previous','Jump',anchors[1]['messageId']),('next','Jump',anchors[2]['messageId']),
        ('next','ReturnToLive',None)]:
        reply = press(key)
        assert (reply['action'],reply['messageId'])==(action,identity), reply
        print(key, json.dumps(reply), flush=True)
        time.sleep(.1)
    call('select',hwnd,'CLICI_FIXTURE_REPLY_3_10')
    time.sleep(.5)
    before = call('snapshot',hwnd,15000)
    reply = press('previous', modifier_order=(0x10, 0x12), release_modifiers_first=True)
    after = call('snapshot',hwnd,15000)
    assert before['selected'] == after['selected'] and any(before['selected']), 'Selection changed or was empty'
    assert before['tail'] == after['tail'], 'Terminal text changed'
    assert not (run/'keys.jsonl').exists(), 'An admitted shortcut leaked keys into the fixture'
    results.append(dict(step='selection',reply=reply,before=before,after=after))
    print('selection',json.dumps(reply),flush=True)
    eligible = False
    time.sleep(.5)
    offset = len(results)
    key_input('previous')
    time.sleep(.5)
    assert results[offset:] and all(not r.get('handled') and r.get('action') == 'PassThrough' for r in results[offset:])
    received = [json.loads(line) for line in (run/'keys.jsonl').read_text().splitlines()]
    assert any(key['key'] == 'OemComma' for key in received), 'Ineligible shortcut did not reach the fixture'
    results.append(dict(step='ineligible-key-received', keys=received))
    print('ineligible',json.dumps(results[-1]),flush=True)
finally:
    stop.set()
    if worker: worker.join(timeout=3)
    bridge.stdin.close()
    try: bridge.wait(timeout=3)
    except subprocess.TimeoutExpired: bridge.kill(); bridge.wait()
    (run/'evidence').mkdir(exist_ok=True)
    (run/('evidence/input-live-frames.json' if args.frames else 'evidence/input-live.json')).write_text(json.dumps(results,indent=2),encoding='utf-8')
