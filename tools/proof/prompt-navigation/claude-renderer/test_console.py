"""Exercise the real classic renderer in an owned ConPTY; no desktop input.

Install requirements-console.txt into an ignored project directory, then pass
--deps and a fresh --run directory. All model traffic goes to a local fake server.
This proves emitted cells, not Windows Terminal COM scrolling or keyboard scope.
"""
import argparse, itertools, json, os, queue, subprocess, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--run', required=True, type=Path)
parser.add_argument('--deps', required=True, type=Path)
parser.add_argument('--claude', required=True, type=Path)
parser.add_argument('--version', default='2.1.296', help='Exact engine version to verify before launch')
parser.add_argument('--reply-delay', type=float, default=0.5, help='Seconds the fake endpoint waits before answering; 0 reproduces the height-0 render of an instant reply')
parser.add_argument('--plugin-env', type=Path, help='Load the plugin from this directory through CLAUDE_CODE_PLUGIN_DIRS, as a deployed shell does, instead of --plugin-dir')
parser.add_argument('--bridge', type=Path, help='Built PromptNavigation.Probe.exe; the plugin spawns it and its observe report is checked against the anchors')
mode = parser.add_mutually_exclusive_group()
mode.add_argument('--control', action='store_true', help='Run stock Claude with no plugin to compare rendering')
mode.add_argument('--passthrough', action='store_true', help='Load hooks with anchors disabled to isolate hook dispatch')
mode.add_argument('--noop', action='store_true', help='Load only one next(e) render hook as a minimal vendor reproduction')
args = parser.parse_args()
RUN = args.run.resolve()
RUN.mkdir(parents=True, exist_ok=False)
sys.path.insert(0, str(args.deps.resolve()))
from winpty import PtyProcess
import pyte

EXE = args.claude.resolve()
if subprocess.check_output([str(EXE),'--version'],text=True).strip() != f'{args.version} (Claude Code)':
    raise RuntimeError('Engine does not match the requested version')
PLUGIN = Path(__file__).resolve().parent
if args.noop: PLUGIN = PLUGIN / 'tests/noop-plugin'
CONFIG = RUN / 'console-config'
CWD = RUN / 'fixture'
CONFIG.mkdir(exist_ok=True)
CWD.mkdir(exist_ok=True)
(CONFIG / '.claude.json').write_text(json.dumps({
    'hasCompletedOnboarding': True, 'theme': 'dark',
    'projects': {str(CWD): {'hasTrustDialogAccepted': True, 'hasCompletedProjectOnboarding': True}},
    'customApiKeyResponses': {'approved': ['clici-local-fixture'], 'rejected': []}
}))

requests = []
request_ids = itertools.count(1)
PROMPT = 'CLICI_FIXTURE_DUPLICATE 日本語 🙂'
class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_): pass
    def do_GET(self):
        self.send_response(200); self.send_header('Content-Type', 'application/json'); self.end_headers()
        self.wfile.write(b'{"data":[]}')
    def do_POST(self):
        request_id = next(request_ids)
        time.sleep(args.reply_delay)
        body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', '0'))))
        requests.append({'path': self.path, 'stream': body.get('stream'), 'responseId': request_id})
        text = '\n'.join(f'CLICI_FIXTURE_REPLY_{i:02}' for i in range(45))
        message = dict(id=f'msg_clici_fixture_{request_id}', type='message', role='assistant', model=body.get('model','claude-fixture'),
                       content=[dict(type='text',text=text)],stop_reason='end_turn',stop_sequence=None,
                       usage=dict(input_tokens=1,output_tokens=1))
        self.send_response(200)
        self.send_header('Content-Type', 'text/event-stream' if body.get('stream') else 'application/json')
        self.send_header('Connection','close'); self.end_headers()
        if not body.get('stream'):
            self.wfile.write(json.dumps(message).encode()); return
        events = [dict(type='message_start', message=dict(message,content=[],stop_reason=None)),
                  dict(type='content_block_start', index=0, content_block=dict(type='text',text='')),
                  dict(type='content_block_delta', index=0, delta=dict(type='text_delta',text=text)),
                  dict(type='content_block_stop', index=0),
                  dict(type='message_delta', delta=dict(stop_reason='end_turn',stop_sequence=None),usage=dict(output_tokens=1)),
                  dict(type='message_stop')]
        for event in events:
            self.wfile.write(('event: '+event['type']+'\ndata: '+json.dumps(event)+'\n\n').encode()); self.wfile.flush()

server = ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
env = {k:v for k,v in os.environ.items() if k.upper() in {
    'PATH','SYSTEMROOT','WINDIR','COMSPEC','TEMP','TMP','USERPROFILE','APPDATA','LOCALAPPDATA',
    'HOMEDRIVE','HOMEPATH','PROGRAMFILES','PROGRAMFILES(X86)','PATHEXT','USERNAME'}}
env.update(CLAUDE_CONFIG_DIR=str(CONFIG),CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC='1',
           DISABLE_AUTOUPDATER='1',CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN='1',
           ANTHROPIC_API_KEY='clici-local-fixture',ANTHROPIC_BASE_URL=f'http://127.0.0.1:{server.server_port}',
           CLICI_CLAUDE_ANCHORS='1',CLICI_CLAUDE_ANCHOR_REPORT=str(RUN/'anchors.json'),
           FORCE_COLOR='3',COLORTERM='truecolor',TERM='xterm-256color')
if args.passthrough: env['CLICI_CLAUDE_ANCHORS']='0'
if args.bridge: env.update(CLICI_RENDERER_BRIDGE=str(args.bridge.resolve()),CLICI_RENDERER_REPORT=str(RUN/'bridge-report.json'))
argv=[str(EXE),'--plugin-dir',str(PLUGIN),'--tools','','--strict-mcp-config','--no-chrome',
      '--setting-sources','user','--disable-slash-commands','--permission-mode','plan',PROMPT]
if args.control: del argv[1:3]
if args.plugin_env: del argv[1:3]; env['CLAUDE_CODE_PLUGIN_DIRS']=str(args.plugin_env.resolve())
proc = PtyProcess.spawn(argv,cwd=str(CWD),env=env,dimensions=(38,105))
screen=pyte.HistoryScreen(105,38,history=5000)
stream=pyte.Stream(screen)
pieces=queue.Queue()
def reader():
    while proc.isalive():
        try: pieces.put(proc.read(65536))
        except (EOFError, OSError): break
threading.Thread(target=reader,daemon=True).start()
raw=[]
trusted=False
imports_declined=False
sent = 1
reply_seen_at = None
reply_count = 0
draft_written = False
passed = False
try:
    deadline=time.monotonic()+60
    while time.monotonic()<deadline and proc.isalive():
        try: piece=pieces.get(timeout=.1)
        except queue.Empty: piece=''
        if piece:
            raw.append(piece); stream.feed(piece)
        (RUN/'console-progress.txt').write_text('\n'.join(screen.display),encoding='utf8')
        if not trusted and 'Yes, I trust this folder' in '\n'.join(screen.display):
            assert ''.join(str(CWD).split()) in ''.join(''.join(screen.display).split())
            time.sleep(.5)
            proc.write('\x1b[B')
            time.sleep(.2)
            proc.write('\r'); trusted=True
        if not imports_declined and 'Allow external CLAUDE.md file imports?' in '\n'.join(screen.display):
            time.sleep(.4)
            proc.write('\r'); imports_declined=True
        if '\x1b[6n' in piece: proc.write(f'\x1b[{screen.cursor.y+1};{screen.cursor.x+1}R')
        if '\x1b[c' in piece: proc.write('\x1b[?1;2c')
        if '\x1b]10;?' in piece: proc.write('\x1b]10;rgb:ffff/ffff/ffff\x1b\\')
        if '\x1b]11;?' in piece: proc.write('\x1b]11;rgb:0000/0000/0000\x1b\\')
        count=''.join(raw).count('CLICI_FIXTURE_REPLY_44')
        if count > reply_count:
            reply_seen_at=time.monotonic(); reply_count=count
        if reply_seen_at is not None and time.monotonic()-reply_seen_at > 1:
            if sent < 3:
                proc.write(PROMPT)
                time.sleep(.1)
                proc.write('\r')
                sent += 1; reply_seen_at=None
            elif not draft_written:
                proc.write('CLICI_FIXTURE_UNSENT_DRAFT')
                draft_written=True; reply_seen_at=time.monotonic()
            else:
                break
    # Preserve the original VT stream: Windows text-mode newline translation
    # would otherwise turn CRLF into CRCRLF and corrupt a replay.
    (RUN/'console-output.txt').write_bytes(''.join(raw).encode('utf8'))
    (RUN/'console-screen.txt').write_text('\n'.join(screen.display),encoding='utf8')
    lines=list(screen.history.top)+[screen.buffer[i] for i in range(screen.lines)]
    # Sparse screen rows still contain blank cells between written columns.
    cells=[[{'text':line[i].data,'fg':line[i].fg} for i in range(screen.columns)] for line in lines]
    (RUN/'console-cells.json').write_text(json.dumps(cells,ensure_ascii=False),encoding='utf8')
    if args.control or args.passthrough or args.noop:
        visible_prompts=sum(PROMPT in ''.join(c['text'] for c in row) for row in cells)
        mode='noop' if args.noop else 'passthrough' if args.passthrough else 'stock'
        result={'control':mode,'version':args.version,'promptRows':visible_prompts,'requests':len(requests),
                'classic':'\x1b[?1049h' not in ''.join(raw)}
        (RUN/'console-result.json').write_text(json.dumps(result,indent=2),encoding='utf8')
        print(json.dumps(result))
        assert visible_prompts == 3, f'{mode} renderer did not retain all three prompts'
        sys.exit(0)
    report=json.loads((RUN/'anchors.json').read_text())
    (RUN/'anchors-observed.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    assert len(report['prompts']) == 3, 'Expected three distinct native prompt IDs'
    assert len({p['messageId'] for p in report['prompts']}) == 3
    assert len({p['nonce'] for p in report['prompts']}) == 3
    positions=[]
    endpoints=[]
    for prompt in report['prompts']:
        pair=[]
        for suffix in ('a1b2','c3d4'):
            encoded=(prompt['nonce']+suffix).lower()
            colors=[encoded[i:i+6] for i in range(0,36,6)]
            found=[]
            for row_index,row in enumerate(cells):
                for col in range(len(row)-5):
                    sample=row[col:col+6]
                    if [c['fg'] for c in sample] == colors:
                        assert ''.join(c['text'] for c in sample) == '      '
                        found.append(row_index)
            pair.append(found)
        endpoints.append({'messageId':prompt['messageId'],'start':pair[0],'end':pair[1]})
        if all(len(matches)==1 for matches in pair): positions.append([pair[0][0],pair[1][0]])
    complete=len(positions)==3 and all(a < b for a,b in positions)
    ordered=all(a[1] < b[0] for a,b in zip(positions,positions[1:]))
    draft='CLICI_FIXTURE_UNSENT_DRAFT' in '\n'.join(screen.display)
    classic='\x1b[?1049h' not in ''.join(raw)
    bridge=None
    if args.bridge:
        # The helper writes its last observe request after a fresh frame; the
        # plugin must have named this engine process, session and bindings.
        bridge=json.loads((RUN/'bridge-report.json').read_text(encoding='utf8'))
        bridge['checks']={'parent':bridge['processId']==proc.pid,'observe':bridge['direction']=='observe',
                          'session':bridge['sessionId']==report['sessionId'],'owned':bridge['composerOwnsInput'] is True,
                          'prompts':bridge['prompts']==report['prompts'],'lease':0 < bridge['deadline']-bridge['inputObservedAt'] <= 750}
    passed=complete and ordered and draft and classic and (bridge is None or all(bridge['checks'].values()))
    result={'requests':requests,'alive':proc.isalive(),'rawCharacters':sum(map(len,raw)),
            'version':args.version,'passed':passed,'prompts':3,'anchorEndpoints':endpoints,
            'renderRequests':report['renderRequests'],'classic':classic,'draftPreserved':draft,
            'bridge':bridge,'screen':'\n'.join(screen.display)}
    (RUN/'console-result.json').write_text(json.dumps(result,indent=2),encoding='utf8')
    print(json.dumps({k:v for k,v in result.items() if k != 'screen'},ensure_ascii=True))
    assert passed, 'Native renderer did not retain three complete, ordered anchor pairs; see console-result.json'
finally:
    proc.close(force=True)
    server.shutdown()
