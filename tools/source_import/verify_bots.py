"""Run the real Godot offline and two-process LAN bot checks with isolated config."""
import argparse
from pathlib import Path
import subprocess
import threading
import time

parser = argparse.ArgumentParser()
parser.add_argument('--godot', required=True, type=Path)
parser.add_argument('--lan', action='store_true')
parser.add_argument('--map', choices=['de_dust2', 'de_inferno', 'de_cpl_strike', 'de_cache'], default='de_dust2')
args = parser.parse_args()
project = Path(__file__).resolve().parents[2]
command = [str(args.godot), '--headless', '--audio-driver', 'Dummy', '--path', str(project),
           'res://scenes/debug/bot_checks.tscn', '--', '--menu-test-profile', '--console-test-profile', '--lan-port=27952',
           '--bot-map=' + args.map]
processes = []
ready = threading.Event()
errors = []
def start(role):
    process = subprocess.Popen(command + ['--bot-role=' + role], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, encoding='utf-8', errors='replace', creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    processes.append(process)
    def output():
        for line in process.stdout:
            print(role + ': ' + line.rstrip(), flush=True)
            if 'BOT_HOST_READY' in line:
                ready.set()
            if 'ERROR:' in line or 'failures=1' in line:
                errors.append(line.strip())
    thread = threading.Thread(target=output, daemon=True)
    thread.start()
    return process, thread
try:
    host, host_thread = start('host' if args.lan else 'offline')
    if args.lan:
        deadline = time.monotonic() + 70
        while not ready.wait(.2):
            if host.poll() is not None or time.monotonic() > deadline:
                raise RuntimeError('Host did not become ready')
        client, client_thread = start('client')
        if client.wait(timeout=80):
            raise RuntimeError('Client checks failed')
        client_thread.join(timeout=2)
    if host.wait(timeout=100):
        raise RuntimeError('Host/offline checks failed')
    host_thread.join(timeout=2)
    if errors:
        raise RuntimeError('Godot errors: ' + '; '.join(errors[:5]))
finally:
    for process in processes:
        if process.poll() is None:
            process.kill()
            process.wait()
