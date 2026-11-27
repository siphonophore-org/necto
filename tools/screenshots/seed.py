"""The demo data for the Necto screenshots, seeded through Necto's own routes. server.py runs it.

The server is Living Room, run by alex. Its people are jordan, sam, riley, casey and morgan, in the Family, Friends
and Kids groups. Every address is under example.com, and every webhook points at example.com.
"""
import base64
import secrets
import time
import urllib.parse

PLUGIN_ID = 'a8b5ecb0-fc00-428b-85e8-74b85820166d'
EVERYONE_ID = '5a1f0c7e-0000-4000-8000-000000000001'
USERS = ['jordan', 'sam', 'riley', 'casey', 'morgan']

# Titles TMDb knows, requested by the people above. Old films, so the posters are ones anyone may show.
REQUESTS = [
    ('riley', 'Movie', 'Metropolis', 1927, '19', 'A city of the future splits into those who plan and those who toil.'),
    ('jordan', 'Movie', 'Nosferatu', 1922, '653', 'An estate agent travels to the castle of a strange count.'),
    ('casey', 'Movie', 'The General', 1926, '961', 'A train engineer chases his stolen locomotive across enemy lines.'),
    ('sam', 'Movie', 'Sherlock Jr.', 1924, '992', 'A projectionist dreams himself into the film he is showing.'),
    ('riley', 'Movie', 'The Kid', 1921, '10098', 'A tramp takes in an abandoned child.'),
]


def log(text):
    print(text, flush=True)


def config(api):
    return api.get('/Plugins/%s/Configuration' % PLUGIN_ID)


def save_config(api, cfg):
    api.post('/Plugins/%s/Configuration' % PLUGIN_ID, cfg)


def libraries(api):
    """Movies and Shows, scanned so Issues has items."""
    have = {f['Name'] for f in api.get('/Library/VirtualFolders')}
    for name, kind, path in (('Movies', 'movies', '/media/movies'), ('Shows', 'tvshows', '/media/shows')):
        if name in have:
            continue
        query = urllib.parse.urlencode({'name': name, 'collectionType': kind, 'paths': path, 'refreshLibrary': 'true'})
        api.post('/Library/VirtualFolders?' + query, {'LibraryOptions': {'EnableRealtimeMonitor': False, 'MetadataSavers': [], 'TypeOptions': []}})
    end = time.time() + 120
    while time.time() < end:
        items = api.get('/Items?Recursive=true&IncludeItemTypes=Movie,Episode')['Items']
        if len(items) >= 6:
            return items
        time.sleep(3)
    return api.get('/Items?Recursive=true&IncludeItemTypes=Movie,Episode')['Items']


def users(api, state):
    """The people, each with a password kept in the state so the shots can sign in as one."""
    found = {u['Name']: u for u in api.get('/Users')}
    state.setdefault('users', {})
    for name in USERS:
        entry = state['users'].get(name) or {'password': secrets.token_urlsafe(12)}
        if name not in found:
            found[name] = api.post('/Users/New', {'Name': name, 'Password': entry['password']})
        else:
            api.post('/Users/%s/Password' % found[name]['Id'], {'NewPw': entry['password']})
        entry['id'] = found[name]['Id']
        state['users'][name] = entry
    return {n: state['users'][n]['id'] for n in USERS}


def sign_in(api_class, base, state, name):
    api = api_class(base)
    auth = api.post('/Users/AuthenticateByName', {'Username': name, 'Pw': state['users'][name]['password']})
    api.token = auth['AccessToken']
    state['users'][name]['token'] = auth['AccessToken']
    return api


def enable_tools(api):
    """Issues, Invites, Pages, the proxy and WireGuard are off until turned on."""
    cfg = config(api)
    cfg['Issues']['Enabled'] = True
    cfg['InviteSettings']['Enabled'] = True
    cfg['CustomPages']['Enabled'] = True
    cfg['WireGuard']['Enabled'] = True
    cfg['WireGuard']['HostEnabled'] = True
    cfg['WireGuard']['PublicEndpoint'] = 'vpn.example.com'
    cfg['WireGuard']['DnsEnabled'] = True
    cfg['InviteSettings']['LinkUrl'] = 'https://media.example.com'
    save_config(api, cfg)


def groups(api, ids):
    have = {g['Name']: g for g in api.get('/Groups')}
    made = {}

    def perms(requests, issues, invites, wireguard, pages=False, scripts=False, events=None):
        return {
            'Requests': requests, 'Issues': issues, 'Invites': invites, 'WireGuard': wireguard,
            'CustomPages': {'AllPages': pages, 'PageIds': []}, 'Scripts': {'AllScripts': scripts, 'ScriptIds': []},
            'Notifications': {'AllEvents': False, 'EventKeys': events or [], 'BrowserNotifications': True, 'JellyfinMessages': True},
        }

    plans = [
        ('Family', ['jordan', 'sam'], perms(
            {'MayRequest': True, 'Movies': True, 'Series': True, 'Unreleased': True, 'AutoApproveMovies': True, 'AutoApproveSeries': False,
             'MaxOpenRequests': 20, 'MovieQuota': 0, 'SeriesQuota': 0, 'MaxSeasons': 0, 'MaySeeOthers': True, 'MaySeeRequesters': True, 'MayManage': False},
            {'MayReport': True, 'Limit': 10, 'MayManage': True},
            {'MayCreate': True, 'MaxInvites': 3, 'AnyGroup': True, 'TargetGroupIds': []},
            {'MayHavePeers': True, 'MaxPeers': 3, 'DataLimit': 0, 'DataPeriod': 'Month'}, pages=True)),
        ('Friends', ['riley', 'casey'], perms(
            {'MayRequest': True, 'Movies': True, 'Series': True, 'Unreleased': False, 'AutoApproveMovies': False, 'AutoApproveSeries': False,
             'MaxOpenRequests': 5, 'MovieQuota': 3, 'SeriesQuota': 1, 'MaxSeasons': 2, 'MaySeeOthers': False, 'MaySeeRequesters': False, 'MayManage': False},
            {'MayReport': True, 'Limit': 5, 'MayManage': False},
            {'MayCreate': True, 'MaxInvites': 1, 'AnyGroup': False, 'TargetGroupIds': []},
            {'MayHavePeers': True, 'MaxPeers': 1, 'DataLimit': 50 * 1024 ** 3, 'DataPeriod': 'Month'})),
        ('Kids', ['morgan'], perms(
            {'MayRequest': False, 'Movies': True, 'Series': True, 'Unreleased': True, 'AutoApproveMovies': False, 'AutoApproveSeries': False,
             'MaxOpenRequests': 10, 'MovieQuota': 0, 'SeriesQuota': 0, 'MaxSeasons': 0, 'MaySeeOthers': False, 'MaySeeRequesters': False, 'MayManage': False},
            {'MayReport': True, 'Limit': 3, 'MayManage': False},
            {'MayCreate': False, 'MaxInvites': 1, 'AnyGroup': False, 'TargetGroupIds': []},
            {'MayHavePeers': False, 'MaxPeers': 1, 'DataLimit': 0, 'DataPeriod': 'Month'})),
    ]
    for name, members, permissions in plans:
        body = dict(have.get(name) or {'Name': name})
        body['MemberIds'] = [ids[m] for m in members]
        merged = dict(body.get('Permissions') or {})
        merged.update(permissions)
        body['Permissions'] = merged
        made[name] = api.put('/Groups/%s' % body['Id'], body) if 'Id' in body else api.post('/Groups', body)
    return made


def invites(api, made):
    have = {i.get('Label') for i in api.get('/Invites/List')}
    day = 24 * 3600
    soon = lambda days: time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime(time.time() + days * day))
    plans = [
        {'Label': 'Cousins', 'Message': 'Welcome to the family server. Ask jordan if anything is missing.', 'GroupId': made['Family']['Id'],
         'MaxUses': 4, 'ExpiresAt': soon(14), 'Resources': [{'Title': 'How to install the app', 'Url': 'https://example.com/apps'}]},
        {'Label': 'Book club', 'Message': 'Movie night picks live here.', 'GroupId': made['Friends']['Id'], 'MaxUses': 6, 'ExpiresAt': soon(30), 'Pin': '246810'},
        {'Label': 'Neighbor', 'Message': '', 'GroupId': made['Friends']['Id'], 'MaxUses': 1, 'ExpiresAt': soon(3)},
    ]
    for plan in plans:
        if plan['Label'] not in have:
            api.post('/Invites', plan)


def member_invites(api_class, api, state, made):
    """One invite jordan made on the members' page, for its shot."""
    member = sign_in(api_class, api.base, state, 'jordan')
    mine = member.get('/Invites/Mine') or {}
    if not any(i.get('Label') == 'Game night' for i in mine.get('Invites') or []):
        member.post('/Invites/Mine', {'Label': 'Game night', 'GroupId': made['Friends']['Id'], 'MaxUses': 2, 'ExpiresAt': None, 'Resources': []})


def ddns(api):
    """Three records, updated once a week, so no update runs while the shots are taken. ddns_status fills their last run."""
    if not api.get('/Ddns/Records').get('Records'):
        for record in (
            {'Name': 'Home', 'Provider': 'Cloudflare', 'Hostname': 'media.example.com', 'Zone': 'example.com', 'Password': 'demo-token-not-real', 'UpdateIPv4': True, 'UpdateIPv6': False, 'Proxied': False, 'UpdateEveryMinutes': 10080},
            {'Name': 'Requests', 'Provider': 'Cloudflare', 'Hostname': 'requests.example.com', 'Zone': 'example.com', 'Password': 'demo-token-not-real', 'UpdateIPv4': True, 'UpdateEveryMinutes': 10080},
            {'Name': 'Backup', 'Provider': 'DuckDns', 'Hostname': 'livingroom-example.duckdns.org', 'Password': 'demo-token-not-real', 'UpdateIPv4': True, 'UpdateEveryMinutes': 10080, 'Enabled': False},
        ):
            api.post('/Ddns/Records', record)
    return [r['Record']['Id'] for r in api.get('/Ddns/Records')['Records']]


# The address the demo server shows as its own, from the range kept for documentation.
DEMO_IPV4 = '203.0.113.24'


def ddns_status(ids):
    """The DDNS status file as a run that just pushed the demo address leaves it. server.py writes it while Jellyfin is
    stopped, so the real public address is never shown."""
    now = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
    earlier = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime(time.time() - 3 * 3600))
    records = {}
    for i, rid in enumerate(ids):
        records[rid] = {'LastIPv4': DEMO_IPV4, 'LastIPv6': '', 'LastStatus': '', 'LastAction': 'IP Unchanged' if i else 'Updated',
                        'LastSuccess': True, 'LastCheckedUtc': now, 'LastUpdateUtc': earlier, 'ConsecutiveFailures': 0,
                        'TransientFailures': 0, 'FailedIPv4': '', 'FailedIPv6': ''}
    return {'LastRunUtc': now, 'DetectedIPv4': DEMO_IPV4, 'DetectedIPv6': '', 'DetectionMessage': '', 'ConsecutiveDetectionFailures': 0,
            'DetectionUnhealthyLogged': False, 'PlaintextWarningLogged': False, 'Records': records}


def proxy(api):
    routes = api.get('/Proxy/Routes')
    if (routes.get('Routes') if isinstance(routes, dict) else routes):
        return
    templates = api.get('/Proxy/Templates')
    for template in templates['Templates']:
        if template['Key'] == 'blank':
            continue
        route = template['Route']
        # A self-signed certificate, so no certificate authority is asked about example.com.
        route['Host'] = 'media.example.com' if template['Key'] == 'jellyfin' else 'home.example.com'
        route['Certificate'] = dict(route.get('Certificate') or {}, Mode='SelfSigned')
        api.post('/Proxy/Routes', route)
    settings = api.get('/Proxy/Settings')
    settings['Enabled'] = True
    settings['AcceptTerms'] = True
    api.post('/Proxy/Settings', settings, ok=None)


def wireguard(api, ids):
    if len(api.get('/WireGuard/Peers')) > 1:
        return
    # Each peer brings its own public key, since the keys are made by wireguard-go, which the kit does not download.
    key = lambda: base64.b64encode(secrets.token_bytes(32)).decode()
    for name, user, subnets in (('Phone', 'jordan', []), ('Laptop', 'jordan', []), ('Tablet', 'sam', []), ('Travel router', 'riley', ['192.168.50.0/24'])):
        api.post('/WireGuard/Peers', {'Name': name, 'UserId': ids[user], 'PublicKey': key(), 'RoutedSubnets': subnets, 'Enabled': True})
    api.post('/WireGuard/Connections', {'Name': 'Cabin', 'Endpoint': 'cabin.example.com:51820', 'PeerPublicKey': key(), 'NewPrivateKey': key(), 'Address': '10.77.0.2/32', 'Enabled': True}, ok=None)


def scripts_and_pages(api):
    cfg = config(api)
    if not cfg['Scripts']['Scripts']:
        cfg['Scripts']['Scripts'] = [
            {'Id': '6d3c1a2e-1b7f-4c55-9a61-000000000001', 'Name': 'Welcome new users', 'Description': 'Sends a hello on a first sign in.', 'Enabled': True,
             'Triggers': ['UserCreated'], 'Operations': [{'Type': 'SendMessage', 'Title': 'Welcome', 'Text': 'Hi {{UserName}}, enjoy the server.', 'Target': 'EventSession', 'DisplaySeconds': 10}]},
            {'Id': '6d3c1a2e-1b7f-4c55-9a61-000000000002', 'Name': 'Nightly scan', 'Description': 'Scans every library at 3 AM.', 'Enabled': True,
             'Triggers': ['Schedule'], 'ScheduleMode': 'Daily', 'ScheduleTime': '03:00', 'Operations': [{'Type': 'ScanLibraries'}]},
            {'Id': '6d3c1a2e-1b7f-4c55-9a61-000000000003', 'Name': 'Too many streams', 'Description': 'Posts to the house chat when four streams run.', 'Enabled': True,
             'Triggers': ['StreamCountReached'], 'StreamCountThreshold': 4,
             'Operations': [{'Type': 'HttpRequest', 'Method': 'POST', 'Url': 'https://hooks.example.com/house-chat', 'Body': '{"text": "{{StreamCount}} streams running"}', 'ContentType': 'application/json'}]},
            {'Id': '6d3c1a2e-1b7f-4c55-9a61-000000000004', 'Name': 'Kids bedtime', 'Description': 'Stops playback for morgan at 9 PM.', 'Enabled': False,
             'Triggers': ['Schedule'], 'ScheduleMode': 'Daily', 'ScheduleTime': '21:00', 'Operations': [{'Type': 'StopPlayback'}]},
        ]
    if not cfg['CustomPages']['Pages']:
        cfg['CustomPages']['Pages'] = [
            {'Id': 'house-rules', 'Slug': 'house-rules', 'Title': 'House rules', 'Visibility': 'User', 'Enabled': True,
             'Html': '<h1>House rules</h1><ul><li>Two streams each at a time.</li><li>Ask before requesting a whole series.</li><li>Report broken files with Issues.</li></ul>',
             'Css': 'body { font-family: sans-serif; }'},
            {'Id': 'movie-night', 'Slug': 'movie-night', 'Title': 'Movie night', 'Visibility': 'User', 'Enabled': True,
             'Html': '<h1>Movie night</h1><p>Friday at 8. Vote for the next film below.</p><div id="votes"></div>', 'Js': ''},
            {'Id': 'status', 'Slug': 'status', 'Title': 'Server status', 'Visibility': 'Anonymous', 'Enabled': True,
             'Html': '<h1>Living Room is up</h1><p>Planned downtime is posted here.</p>'},
        ]
    save_config(api, cfg)


def notifications(api):
    settings = api.get('/Notifications/Settings')
    names = {c.get('Name') for c in settings.get('Channels') or []}
    channels = list(settings.get('Channels') or [])
    plans = [
        {'Name': 'House chat', 'Kind': 'Discord', 'Url': 'https://discord.example.com/api/webhooks/1234/demo', 'Username': 'Living Room', 'AllEvents': False,
         'EventKeys': ['requests.created', 'issues.created', 'proxy.banned', 'storage.space_low']},
        {'Name': 'Home automation', 'Kind': 'Webhook', 'Url': 'https://hooks.example.com/jellyfin', 'AllEvents': True, 'EventKeys': []},
        {'Name': 'Admin mail', 'Kind': 'Email', 'Recipients': 'alex@example.com', 'AllEvents': False, 'EventKeys': ['storage.space_low', 'storage.smart_warning', 'ddns.failed', 'wireguard.data-limit']},
    ]
    for plan in plans:
        if plan['Name'] not in names:
            channels.append(dict(plan, Enabled=True))
    settings['Channels'] = channels
    settings['ServerUrl'] = 'https://media.example.com'
    settings['Smtp'] = dict(settings.get('Smtp') or {}, Host='smtp.example.com', Port=587, UseTls=True, FromAddress='jellyfin@example.com', FromName='Living Room')
    api.put('/Notifications/Settings', settings, ok=None)


def requests_and_issues(api_class, api, state, items):
    if not api.get('/Requests/All').get('Items'):
        for who, kind, title, year, tmdb, overview in REQUESTS:
            member = sign_in(api_class, api.base, state, who)
            member.post('/Requests', {'Type': kind, 'Title': title, 'Year': year, 'Overview': overview, 'ProviderIds': {'Tmdb': tmdb}, 'Note': ''}, ok=None)
    if not (api.get('/Issues/All') or {}).get('Items'):
        reports = [('jordan', 'Harbor Lights', 'Audio', 'The sound drifts out of sync after ten minutes.'),
                   ('sam', 'Northern Line', 'Subtitles', 'English subtitles are missing.'),
                   ('riley', 'Fieldnotes', 'WontPlay', 'Episode two stops at the start on the TV.'),
                   ('casey', 'Paper Kites', 'Metadata', 'The poster is from another film.')]
        for who, name, kind, note in reports:
            item = next((i for i in items if name in (i.get('Name') or '') or name in (i.get('SeriesName') or '')), None)
            if item is None:
                continue
            member = sign_in(api_class, api.base, state, who)
            member.post('/Issues', {'ItemId': item['Id'], 'Type': kind, 'Note': note}, ok=None)


def run(api, state, media):
    log('== seeding')
    media(state)
    items = libraries(api)
    ids = users(api, state)
    enable_tools(api)
    made = groups(api, ids)
    state['groups'] = {k: v['Id'] for k, v in made.items()}
    invites(api, made)
    member_invites(type(api), api, state, made)
    state['ddnsRecords'] = ddns(api)
    proxy(api)
    wireguard(api, ids)
    scripts_and_pages(api)
    requests_and_issues(type(api), api, state, items)
    # Last, so the demo events above are not sent to the made up addresses.
    notifications(api)
