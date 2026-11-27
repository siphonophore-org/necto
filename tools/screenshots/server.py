#!/usr/bin/env python3
"""Necto screenshot server: a throwaway Jellyfin in Docker with the plugin built from this checkout and demo data.

Usage:
  python3 -I server.py up      build the plugin, start the container, run the wizard and seed the demo data
  python3 -I server.py seed    seed the demo data again on a running container
  python3 -I server.py down    remove the container and its state

The container is necto-shots, on a free port from 8190 up. It never touches ports 8096 to 8098. The address, the admin
token and the ids the shots need go in .state/server.json, which shots.mjs reads. Only the standard library is used.
"""
import json
import os
import secrets
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
STATE = os.path.join(HERE, '.state')
STATE_FILE = os.path.join(STATE, 'server.json')
NAME = 'necto-shots'
IMAGE = os.environ.get('JELLYFIN_IMAGE', 'jellyfin/jellyfin:12.4')
PLUGIN_ID = 'a8b5ecb0-fc00-428b-85e8-74b85820166d'
OWNER_PORTS = range(8096, 8099)


def log(text):
    print(text, flush=True)


def version():
    """The plugin version from build.yaml."""
    for line in open(os.path.join(REPO, 'build.yaml'), encoding='utf-8'):
        if line.startswith('version:'):
            return line.split(':', 1)[1].strip().strip('"')
    sys.exit('No version in build.yaml')


def free_port():
    """The first port from 8190 up that nothing listens on."""
    for port in range(8190, 8300):
        if port in OWNER_PORTS:
            continue
        with socket.socket() as s:
            try:
                s.bind(('127.0.0.1', port))
                return port
            except OSError:
                continue
    sys.exit('No free port from 8190 to 8299')


def docker(*args, check=True, capture=True):
    return subprocess.run(['docker', *args], check=check, capture_output=capture, text=True)


class Api:
    """A small Jellyfin client over urllib."""

    def __init__(self, base, token=None):
        self.base = base
        self.token = token

    def headers(self):
        auth = 'MediaBrowser Client="Necto shots", Device="kit", DeviceId="necto-shots-kit", Version="1.0"'
        if self.token:
            auth += ', Token="%s"' % self.token
        return {'Authorization': auth, 'Content-Type': 'application/json', 'Accept': 'application/json'}

    def call(self, method, path, body=None, ok=(200, 204)):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=self.headers())
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                status, content = resp.status, resp.read()
        except urllib.error.HTTPError as e:
            status, content = e.code, e.read()
        if ok and status not in ok:
            raise RuntimeError('%s %s returned %s: %s' % (method, path, status, content[:400]))
        if not content:
            return None
        try:
            return json.loads(content)
        except ValueError:
            return content

    def get(self, path, **kw):
        return self.call('GET', path, **kw)

    def post(self, path, body=None, **kw):
        return self.call('POST', path, body if body is not None else {}, **kw)

    def put(self, path, body=None, **kw):
        return self.call('PUT', path, body if body is not None else {}, **kw)

    def wait(self, path='/System/Info/Public', timeout=180):
        end = time.time() + timeout
        while time.time() < end:
            try:
                return self.get(path)
            except (urllib.error.URLError, ConnectionError, OSError, RuntimeError):
                time.sleep(2)
        sys.exit('Jellyfin did not answer at %s' % self.base)


def read_state():
    if not os.path.isfile(STATE_FILE):
        sys.exit('No server. Run python3 -I server.py up first.')
    return json.load(open(STATE_FILE, encoding='utf-8'))


def write_state(state):
    os.makedirs(STATE, exist_ok=True)
    with open(STATE_FILE, 'w', encoding='utf-8') as f:
        json.dump(state, f, indent=2)


def build():
    """Runs build.sh and returns the zip it writes."""
    log('== building the plugin')
    subprocess.run(['./build.sh'], cwd=REPO, check=True, stdout=subprocess.DEVNULL)
    path = os.path.join(REPO, 'dist', 'necto-%s.zip' % version())
    if not os.path.isfile(path):
        sys.exit('No zip at %s' % path)
    return path


def media(state):
    """Short test films and an episode, made with the image's ffmpeg, so Issues has items to point at."""
    films = ['Harbor Lights (2019)', 'The Quiet Orchard (2021)', 'Northern Line (2017)', 'Paper Kites (2022)']
    script = ['set -e']
    for film in films:
        script.append('mkdir -p "/media/movies/%s"' % film)
        script.append('[ -f "/media/movies/%s/%s.mkv" ] || /usr/lib/jellyfin-ffmpeg/ffmpeg -loglevel error -f lavfi -i testsrc=size=320x180:rate=24 -t 3 -c:v libx264 "/media/movies/%s/%s.mkv"' % (film, film, film, film))
    for ep in ('S01E01', 'S01E02'):
        script.append('mkdir -p "/media/shows/Fieldnotes/Season 01"')
        script.append('[ -f "/media/shows/Fieldnotes/Season 01/Fieldnotes %s.mkv" ] || /usr/lib/jellyfin-ffmpeg/ffmpeg -loglevel error -f lavfi -i testsrc=size=320x180:rate=24 -t 3 -c:v libx264 "/media/shows/Fieldnotes/Season 01/Fieldnotes %s.mkv"' % (ep, ep))
    docker('exec', NAME, 'sh', '-c', '\n'.join(script))


def up():
    if docker('ps', '-a', '-q', '-f', 'name=^%s$' % NAME).stdout.strip():
        log('== removing the old container')
        docker('rm', '-f', NAME)
    zip_path = build()
    staged = os.path.join(STATE, 'plugins')
    subprocess.run(['rm', '-rf', staged], check=True)
    folder = os.path.join(staged, 'Necto_%s' % version())
    os.makedirs(folder)
    zipfile.ZipFile(zip_path).extractall(folder)
    # The address lookups point at example.com before the first start, so the server's real public address is never
    # looked up or shown. seed.ddns_status gives it the demo address instead.
    configs = os.path.join(staged, 'configurations')
    os.makedirs(configs)
    with open(os.path.join(configs, 'Jellyfin.Plugin.Necto.xml'), 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<PluginConfiguration><Ddns>'
                '<IPv4DetectionUrl>https://ip.example.com</IPv4DetectionUrl><IPv6DetectionUrl>https://ip.example.com</IPv6DetectionUrl>'
                '</Ddns></PluginConfiguration>\n')

    port = free_port()
    log('== starting %s on port %s' % (NAME, port))
    docker('create', '--name', NAME, '-p', '127.0.0.1:%s:8096' % port, IMAGE)
    docker('cp', staged, '%s:/config/' % NAME)
    docker('start', NAME)

    api = Api('http://127.0.0.1:%s' % port)
    info = api.wait()
    password = secrets.token_urlsafe(18)
    if not info.get('StartupWizardCompleted'):
        log('== running the startup wizard')
        api.wait('/Startup/Configuration')
        api.post('/Startup/Configuration', {'UICulture': 'en-US', 'MetadataCountryCode': 'US', 'PreferredMetadataLanguage': 'en', 'ServerName': 'Living Room'})
        api.get('/Startup/User')
        api.post('/Startup/User', {'Name': 'alex', 'Password': password})
        api.post('/Startup/RemoteAccess', {'EnableRemoteAccess': True, 'EnableAutomaticPortMapping': False})
        api.post('/Startup/Complete')
        time.sleep(2)
    auth = api.post('/Users/AuthenticateByName', {'Username': 'alex', 'Pw': password})
    state = {'port': port, 'url': api.base, 'password': password, 'token': auth['AccessToken'], 'userId': auth['User']['Id'],
             'serverId': auth['ServerId'], 'serverName': 'Living Room'}
    write_state(state)
    import seed
    seed.run(Api(api.base, state['token']), state, media)
    write_state(state)
    settle(api, state)
    log('== ready at %s' % api.base)


def settle(api, state):
    """Writes the DDNS status while Jellyfin is stopped, then starts it again and waits for it."""
    import seed
    log('== writing the DDNS status and restarting')
    path = os.path.join(STATE, 'Jellyfin.Plugin.Necto.Ddns.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(seed.ddns_status(state['ddnsRecords']), f, indent=2)
    docker('stop', NAME)
    docker('cp', path, '%s:/config/plugins/configurations/Jellyfin.Plugin.Necto.Ddns.json' % NAME)
    docker('start', NAME)
    api.wait()
    Api(api.base, state['token']).wait('/System/Info')
    time.sleep(5)


def reseed():
    state = read_state()
    import seed
    seed.run(Api(state['url'], state['token']), state, media)
    write_state(state)
    settle(Api(state['url']), state)


def down():
    docker('rm', '-f', NAME, check=False)
    subprocess.run(['rm', '-rf', STATE], check=True)
    log('== removed %s' % NAME)


if __name__ == '__main__':
    sys.path.insert(0, HERE)
    command = sys.argv[1] if len(sys.argv) > 1 else 'up'
    {'up': up, 'seed': reseed, 'down': down}.get(command, lambda: sys.exit(__doc__))()
