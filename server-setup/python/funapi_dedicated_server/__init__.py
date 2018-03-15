# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2018 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

from gevent import monkey
monkey.patch_all()


import base64
import copy
import datetime
import json
from functools import wraps
import logging
import os
import sys
import time
import urllib
import urlparse

from flask import abort as flask_abort
from flask import Flask, jsonify, request, make_response

import gevent
import gevent.event
import gevent.lock

import redis
import requests

from funapi_dedicated_server.exception import (
      MatchAlreadyCreatedException,
      MatchNotFoundException,
      TooManyDedicatedServerException,
    )


DEDI_SERVER_KEY = 'ife-dedi-hosts'
DEDI_MATCH_KEY = 'ife-dedi-matches'

server_id = None
manager = None  # instance of ServerManager or its derived class


logging.basicConfig(level=logging.INFO, format='%(asctime)s %(message)s')
log = logging.getLogger('funapi_dedicated_server')
log.setLevel(logging.INFO)


# NOTE: DSM will allocate neighboring ports to dedicated server.
# i.e. It will allocate 7500 as dedi-server port, and it will also allocate
# 7501 as a beacon port
PORT_POOL_SIZE = 500

_NULL_UUID = '00000000-0000-0000-0000-000000000000'


class StateProxy(object):
  def __init__(self):
    pass

  def update_server_state(self, server_id, server_state):
    raise NotImplementedError('StateProxy.update_server_state')

  def update_match_state(self, match_id, match_dict):
    raise NotImplementedError('StateProxy.update_match_state')

  def remove_match_state(self, match_id):
    raise NotImplementedError('StateProxy.remove_match_state')

  def get_auth_header(self):
    return {}


class RedisStateProxy(StateProxy):
  def __init__(self, redis_host='localhost', redis_port=6379, redis_auth=None):
    self.address = (redis_host, redis_port)
    self.auth = redis_auth

    self._conn = redis.StrictRedis(host=redis_host, port=redis_port,
        password=redis_auth, retry_on_timeout=True)

  def update_server_state(self, server_id, server_state):
    self._conn.hset(DEDI_SERVER_KEY, server_id, server_state)

  def update_match_state(self, match_id, match_state):
    self._conn.hset(DEDI_MATCH_KEY, match_id, match_state)

  def remove_match_state(self, match_id):
    self._conn.hdel(DEDI_MATCH_KEY, match_id)


class StateApiProxy(StateProxy):
  def __init__(self, oauth_token_url, oauth_client_id, oauth_client_secret,
      oauth_content_type, service_endpoint):
    self.base_url = service_endpoint
    self.token_url = oauth_token_url
    id_pw = oauth_client_id + ':' + oauth_client_secret
    self.auth_header = 'Basic ' + base64.b64encode(id_pw)
    self.token = None
    self.expiration = datetime.datetime.now()
    self.oauth_content_type = oauth_content_type
    self._refresh_token()

  def _refresh_token(self):
    try:
      headers={
          'Authorization': self.auth_header,
          'Content-Type': self.oauth_content_type,
        }
      res = requests.post(self.token_url, headers=headers, data='')
      res.raise_for_status()

      token = json.loads(res.content)
      self.token = token['access_token']
      t = token['expires_in'] - 30
      self.expiration = datetime.datetime.now() + datetime.timedelta(seconds=t)
    except:
      import traceback
      log.error('Failed to refresh OAuth token - {}'.format(
          traceback.format_exc()))

  def _ensure_token(self):
    now = datetime.datetime.now()
    if self.expiration <= now:
      self._refresh_token()

  def update_server_state(self, server_id, server_state):
    self._ensure_token()
    _id = urllib.quote(server_id, safe='')
    url = urlparse.urljoin(self.base_url, 'host/{}/'.format(_id))
    headers = {
        'Authorization': 'Bearer ' + self.token,
        'Content-Type': 'application/json',
      }
    res = requests.post(url, headers=headers, data=server_state)
    res.raise_for_status()

  def update_match_state(self, match_id, match_state):
    self._ensure_token()
    url = urlparse.urljoin(self.base_url, 'match/{}/'.format(match_id))
    headers = {
        'Authorization': 'Bearer ' + self.token,
        'Content-Type': 'application/json',
      }
    res = requests.post(url, headers=headers, data=match_state)
    res.raise_for_status()

  def remove_match_state(self, match_id):
    self._ensure_token()
    url = urlparse.urljoin(self.base_url, 'match/{}/'.format(match_id))
    headers = {
        'Authorization': 'Bearer ' + self.token,
      }
    res = requests.delete(url, headers=headers)
    res.raise_for_status()

  def get_auth_header(self):
    self._ensure_token()
    headers = {
        'Authorization': 'Bearer ' + self.token,
    }
    return headers


class ServerManager(object):
  def __init__(self, game_ip='localhost', rest_ip='localhost', rest_port=5000,
      exe_path=None, use_beacon=False, external_url=None, engine_url=None,
      heartbeat=10, verbose=False, concurrent_servers=1, instance_id=None,
      region=None, max_ds_uptime_seconds=0, state_proxy=None, **kwargs):
    global server_id

    if external_url:
      if external_url[-1] == '/':
        external_url = external_url[:-1]
      server_id = external_url
    else:
      server_id = 'http://{0}:{1}'.format(rest_ip, rest_port)

    self.engine_url = engine_url
    if self.engine_url and self.engine_url[-1] != '/':
      self.engine_url += '/'

    log.info('Server should be accessible at {0}'.format(server_id))
    self.version = None
    self.verbose = verbose

    self.game_ip = game_ip
    self.rest_ip = rest_ip
    self.rest_port = rest_port
    self.heartbeat_interval = heartbeat
    if use_beacon:
      self.port_pool = list(xrange(7500, 7500 + PORT_POOL_SIZE * 2, 2))
    else:
      self.port_pool = list(xrange(7500, 7500 + PORT_POOL_SIZE))
    self.use_beacon = use_beacon
    self.concurrent_servers = concurrent_servers

    self.instance_id = instance_id
    self.region = region

    self.max_ds_uptime_seconds = max_ds_uptime_seconds

    self.state_proxy = state_proxy
    assert self.state_proxy

    self.exe_path = exe_path
    if exe_path is None:
      log.error('exe_path for dedicated server executable is not set')
      raise RuntimeError('exe_path for dedicated server executable is not set')

    if not os.path.exists(self.exe_path):
      log.error('Cannot find dedicated server binary: ' + self.exe_path)
      raise RuntimeError('Canont find dedicated_server binary')

    self.lock = gevent.lock.Semaphore()
    # 게임 서버 쪽에서 보낸 게임 데이터
    self.match_data = {}
    # 게임 서버가 시작되었는지 확인할 이벤트
    self.match_started = {}
    # 매치 인스턴스 정보 (id -> (pid, port))
    self.match_instances = {}
    # 게임 서버한테 보고를 보낼 주소; (id -> (host, port))
    self.match_origins = {}

    # 유저 난입 처리
    self.pending_data = dict()
    self.pending_users = {}
    self.pending_user_event = {}

    #self._do_push_server_state()
    self._do_check_heartbeats()

    self._version_check_event = None

    log.info('ServerManager initialized: server_id: ' + server_id)

  def set_version(self, version_str):
    with self.lock:
      if self.version is None:
        self.version = version_str
        log.info('Dedicated server version: {0}'.format(self.version))
      else:
        log.error('Version already set. Current={0}, new={1}'.format(
            self.version, version_str))
        return False
    self._do_push_server_state()
    return True

  def check_version(self):
    with self.lock:
      if self.version is not None:
        log.error('Already set version')
        return
      self._version_check_event = gevent.event.Event()
      event = self._version_check_event

    data = {
        'args': ['-FunapiVersion']
    }
    pid = self.spawn_match(_NULL_UUID, self.port_pool[-1], 0, data, 30)
    log.info('Starting client to check version. (pid={0})'.format(pid))

    def wait_for_result():
      # NOTE(jinuk): spawn_match 최대 실행 시간과 일치하는 값으로 대기해야 한다.
      event.wait(30)
      with self.lock:
        if self.version is None:
          log.error('Cannot get version from dedicated server')
          sys.exit(0)
      self._version_check_event = None

    gevent.spawn(wait_for_result)

  def create_match(self, match_id, data):
    log.info('ServerManager create_match({})'.format(match_id))

    port = data['port']
    data['port'] = self.rest_port
    data['created'] = int(time.time())
    event = gevent.event.Event()
    with self.lock:
      if match_id in self.match_data:
        raise MatchAlreadyCreatedException()
      if len(self.match_data) >= self.concurrent_servers:
        raise TooManyDedicatedServerException()

      self.match_origins[match_id] = (request.remote_addr, port)
      self.match_data[match_id] = data
      self.match_started[match_id] = event
      self.pending_data[match_id] = []
      self.pending_users[match_id] = []
      self.pending_user_event[match_id] = []  # 여러 개의 이벤트가 있을 수 있다

      # FIXME(jinuk): port_pool 에 포트 데이터 반납할 것
      port = self.port_pool.pop(0)
      beacon_port = 0
      if self.use_beacon:
        beacon_port = port + 1

    # 프로세스 생성
    log.info('ServerManager spawn_match({})'.format(match_id))
    pid = self.spawn_match(match_id, port, beacon_port, data,
                           self.max_ds_uptime_seconds)

    with self.lock:
      inst_data = {'pid': pid, 'port': port}
      if beacon_port >  0:
        inst_data['beacon_port'] = beacon_port
      self.match_instances[match_id] = inst_data
      all_matches = self.match_instances.copy()

    self._push_server_state(all_matches)

    return event

  def spawn_match(self, match_id, port, beacon_port, heartbeat, data,
                  max_ds_uptime_seconds):
    '''match 를 생성하고 port 번호와 pid를 반환한다'''
    raise NotImplementedError()

  def get_match_data(self, match_id):
    with self.lock:
      if match_id not in self.match_data:
        return None
      return self.match_data[match_id]

  # 데디케이티드 게임 서버가 준비되면 호출
  def notify_match_spawned(self, match_id):
    event = None

    with self.lock:
      if match_id not in self.match_data:
        return None
      self.match_data[match_id]['last_heartbeat'] = int(time.time())
      event = self.match_started[match_id]
      del self.match_started[match_id]

    event.set()

  def notify_user_joined(self, match_id, user_id):
    uri = self._make_engine_url(match_id, 'user_joined/{0}/'.format(user_id))
    if not uri:
      log.error(
          'Failed to post user join(match={0}): cannot build URI'.format(
              match_id))
      return

    res = requests.post(
        uri, headers=self.state_proxy.get_auth_header(), data={})
    res.raise_for_status()

  def notify_user_left(self, match_id, user_id):
    uri = self._make_engine_url(match_id, 'user_left/{0}'.format(user_id))
    if not uri:
      log.error('Failed to post user left(match={0}): cannot build URI'.format(
              match_id))
      return

    res = requests.post(
        uri, headers=self.state_proxy.get_auth_header(), data={})
    res.raise_for_status()

  def send_engine_callback(self, match_id, data):
    uri = self._make_engine_url(match_id, 'callback/')
    if not uri:
      log.error('Failed to post callback(match={0}): cannot build URI'.format(
              match_id))
      return
    headers = self.state_proxy.get_auth_header()
    headers['Content-Type'] = 'application/json'
    res = requests.post(uri, headers=headers, data=data)
    res.raise_for_status()

  def add_user(self, match_id, data):
    log.info('ServerManager add_user({})'.format(match_id))
    event = gevent.event.Event()
    with self.lock:
      if match_id not in self.pending_users:
        raise MatchNotFoundException()

      if 'match_data' not in data:
        self.pending_data[match_id].append({})
      else:
        self.pending_data[match_id].append(data['match_data'])

      for i, user in enumerate(data['users']):
        ud = data['user_data'][i]
        if not ud:
          ud = {}
        self.pending_users[match_id].append(
            (user.copy(), ud.copy(),))
        self.pending_user_event[match_id].append(event)
    return event

  def get_pending_user(self, match_id):
    log.info('ServerManager get_pending_user({})'.format(match_id))
    with self.lock:
      if match_id not in self.pending_users:
        return None, []

      if not self.pending_users[match_id]:
        return None, []

      users = self.pending_users[match_id]
      events = self.pending_user_event[match_id]
      match_data = self.pending_data[match_id]

      self.pending_users[match_id] = []
      self.pending_user_event[match_id] = []
      self.pending_data[match_id] = []

    # notify
    for event in events:
      event.set()
    return users, match_data

  def notify_match_finished(self, match_id):
    if match_id == _NULL_UUID:
      if self._version_check_event:
        self._version_check_event.set()
      return
    self._cleanup_match_data(match_id)

  def _cleanup_match_data(self, match_id):
    with self.lock:
      if match_id not in self.match_instances:
        log.error('Match {}: cannot clean up; not found'.format(match_id))
        return

      if match_id in self.match_data:
        port = self.match_data[match_id]['port']
        del self.match_data[match_id]
        if port > 0:
          self.port_pool.append(port)

      if match_id in self.match_origins:
        del self.match_origins[match_id]

      if match_id in self.pending_users:
        del self.pending_users[match_id]

      del self.match_instances[match_id]
      all_matches = self.match_instances.copy()

    #self.redis_conn.hdel(DEDI_MATCH_KEY, match_id)
    self.state_proxy.remove_match_state(match_id)
    self._push_server_state(all_matches)

  def _make_engine_url(self, match_id, suffix):
    if self.engine_url:
      _PATH = '{0}/'.format(match_id)
      prefix = self.engine_url
    else:
      _PATH = 'v1/dedicated_server/{0}/'.format(match_id)
      with self.lock:
        if match_id not in self.match_origins:
          if not self.match_origins:
            return None
          addr = self.match_origins[self.match_origins.keys()[0]]
        else:
          addr = self.match_origins[match_id]
      prefix = 'http://{0}:{1}/'.format(*addr)

    return urlparse.urljoin(prefix, os.path.join(_PATH, suffix))

  def notify_match_result(self, match_id, result):
    uri = self._make_engine_url(match_id, 'result/')
    if not uri:
      log.error(
          'Failed to post match result (match={0}): cannot build URI'.format(
              match_id))
      return

    headers = self.state_proxy.get_auth_header()
    headers['Content-Type'] = 'application/json'
    res = requests.post(uri, headers=headers, data=result)
    # data cleanup 은 프로세스 종료했을 때만 처리한다.
    res.raise_for_status()

  def get_game_port(self, match_id):
    with self.lock:
      if match_id not in self.match_instances:
        log.error('Match {}: not found'.format(match_id))
        return 0
      return self.match_instances[match_id]['port']

  def _push_server_state(self, matches):
    datum = {
        'matches': matches,
        'max_matches': self.concurrent_servers,
        'ts': int(time.time()),
        'public_ip': self.game_ip,
        'server_version': self.version,
      }

    datum['instance_id'] = self.instance_id or ''
    datum['region'] = self.region or ''
    json_data = json.dumps(datum)
    self.state_proxy.update_server_state(server_id, json_data)

  def _do_push_server_state(self):
    try_count = 1
    while True:
      with self.lock:
        all_matches = self.match_instances.copy()

      if _NULL_UUID in all_matches:
        del all_matches[_NULL_UUID]

      try:
        self._push_server_state(all_matches)
        break
      except redis.ConnectionError as e:
        log.error('Cannot connect to the redis server')
      except:
        import traceback
        log.error('Cannot update redis: ' + traceback.format_exc())

      try_count += 1
      next_try = 2 ** (try_count - 2)
      if next_try > 16:
        next_try = 16
      log.warning('Retry to update data at redis after ' + str(next_try) + 's')
      gevent.sleep(next_try)

    gevent.spawn_later(30, self._do_push_server_state)

  # heartbeat 메세지 받으면 업데이트
  def _update_heartbeat_time(self, match_id):
    with self.lock:
      if match_id in self.match_data:
          self.match_data[match_id]['last_heartbeat'] = int(time.time())

  # heartbeat 체크
  def _do_check_heartbeats(self):
    time_now = int(time.time())
    with self.lock:
      all_match_data = self.match_data.copy()
    for match_id in all_match_data:
      match_data = all_match_data[match_id]
      if 'last_heartbeat' not in match_data:
        continue
      if time_now > match_data['last_heartbeat'] + self.heartbeat_interval * 2.5:
        log.error('Heartbeat for Match({}) missed for too long'.format(match_id))
        self.notify_match_finished(match_id)

    gevent.spawn_later(self.heartbeat_interval, self._do_check_heartbeats)

  # match state 업데이트
  def _update_match_state(self, match_id, state):
    if match_id not in self.match_instances:
      log.error('Match {}: not found'.format(match_id))

      # TODO(jinuk): (re)read from redis and then update
      return

    data = {
      'svr_id': server_id,
      'state': state,
    }
    self.state_proxy.update_match_state(match_id, json.dumps(data))


def make_error_response(status_code, msg):
  headers = {'Content-Type': 'application/json'}
  error_json = json.dumps({
      'status': 'error',
      'error': msg.replace('\n', '\\n').replace('"', '\"'),
    })
  return make_response(error_json, status_code, headers)


def abort(status_code, msg):
  flask_abort(make_response(jsonify(message=msg), status_code))


def token_required(token_endpoint, validators, jwt_verifier):
  def wrapper(fn):
    @wraps(fn)
    def wrapped(*args, **kwargs):
      if not token_endpoint:
        return fn(*args, **kwargs)

      if 'authorization' not in request.headers:
        log.error('Bearer token not found')
        abort(401, "requires access token")

      ah = request.headers['authorization']
      if ah[:7].lower() != 'bearer ':
        log.error('Invalid bearer token')
        abort(401, 'requires valid bearer token')
      token = ah[7:]

      # Verify the token (JWT signature).
      if jwt_verifier and jwt_verifier(token):
        return fn(*args, **kwargs)

      # Verify against the OAuth server.
      try:
        res = requests.get(token_endpoint,
            headers={'authorization': 'Bearer ' + token})
        res.raise_for_status()
        token_info = json.loads(res.content)
        for validator in validators:
          if not validator(token_info):
            log.error('Token verification failed')
            abort(401, 'token validation failed')
      except:
        import traceback
        log.error('server error - {}'.format(traceback.format_exc()))
        abort(500, 'server failure')

      return fn(*args, **kwargs)

    return wrapped

  return wrapper


def from_localhost(fn):
  @wraps(fn)
  def wrapped(*args, **kwargs):
    if request.environ['REMOTE_ADDR'] != '127.0.0.1':
      abort(400, 'not allowed')
    return fn(*args, **kwargs)

  return wrapped


def create_app(token_endpoint=None, jwt_verifier=None):
  app = Flask('DedicatedServerHost')

  @app.route('/')
  def index():
    return 'OK'

  @app.route('/match/<match_id>/', methods=['POST'], strict_slashes=False)
  @token_required(token_endpoint, [], jwt_verifier)
  def create_match(match_id):
    '''start match'''
    try:
      event = manager.create_match(match_id, json.loads(request.data))
      event.wait()

      # FIXME(jinuk): timeout, 이미 있는 경우 처리
      return jsonify(status='OK',
                     host=manager.game_ip,
                     port=manager.get_game_port(match_id))
    except MatchAlreadyCreatedException as e:
      log.error('Match({}) already created'.format(match_id))
      return make_error_response(400, unicode(e))
    except TooManyDedicatedServerException as e:
      log.error('Match({}): too many dedicated servers'.format(match_id))
      return make_error_response(400, unicode(e))
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route('/match/<match_id>/', methods=['PUT'], strict_slashes=False)
  @token_required(token_endpoint, [], jwt_verifier)
  def add_user(match_id):
    '''Add additional users to match'''
    try:
      event = manager.add_user(match_id, json.loads(request.data))
      event.wait()
      return jsonify(status='OK',
                     host=manager.game_ip,
                     port=manager.get_game_port(match_id))
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  # NOTE(jinuk): 상태를 변화시키는 API라 body가 필요하지 않지만 POST로 처리한다
  @app.route('/match/<match_id>/pending_users/', methods=['POST'],
      strict_slashes=False)
  @from_localhost
  def get_pending_user(match_id):
    try:
      users, data_list = manager.get_pending_user(match_id)
      if not users:
        return jsonify(status='OK')

      tokens = []
      data = []
      for u, d in users:
        tokens.append(u)
        data.append(d)
      return jsonify(status='OK',
                     users=tokens,
                     user_data=data,
                     match_data=data_list)
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route('/match/<match_id>/', methods=['GET'], strict_slashes=False)
  @from_localhost
  def get_match_data(match_id):
    data = manager.get_match_data(match_id)
    if not data:
      return make_error_response(404, 'match not found')

    _data = copy.deepcopy(data)
    _data['match_data'] = _data['data']['match_data']
    _data['user_data'] = _data['data']['user_data']
    del _data['data']
    return jsonify(status='ok', data=_data)

  @app.route('/match/<match_id>/ready', methods=['POST'], strict_slashes=False)
  @from_localhost
  def ready_match(match_id):
    try:
      manager.notify_match_spawned(match_id)
      manager._update_match_state(match_id, {})
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route(
      '/match/<match_id>/result/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def report_match_result(match_id):
    try:
      manager.notify_match_result(match_id, request.data)
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route(
      '/match/<match_id>/heartbeat/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def match_heartbeat(match_id):
    try:
      manager._update_heartbeat_time(match_id)
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route('/match/<match_id>/state/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def report_match_state(match_id):
    try:
      game_state = json.loads(request.data)
      manager._update_match_state(match_id, game_state)
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route(
      '/match/<match_id>/callback/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def send_engine_callback(match_id):
    '''Send JSON message to game server'''
    try:
      manager.send_engine_callback(match_id, request.data)
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route(
      '/match/<match_id>/joined/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def match_user_joined(match_id):
    try:
      body = json.loads(request.data)
      manager.notify_user_joined(match_id, body['uid'])
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route(
      '/match/<match_id>/left/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def match_user_left(match_id):
    try:
      body = json.loads(request.data)
      manager.notify_user_left(match_id, body['uid'])
      return jsonify(status='ok')
    except:
      import traceback
      e_str = traceback.format_exc()
      log.error(e_str)
      return make_error_response(500, e_str)

  @app.route('/server/version/', methods=['POST'], strict_slashes=False)
  @from_localhost
  def report_ds_version():
    try:
      body = json.loads(request.data)
      if 'version' not in body:
        log.error('No specified version: {0}'.format(request.data))
        return make_error_response(500, 'Invalid version')
      version_str = body['version']
      if not manager.set_version(version_str):
        return make_error_response(500, 'Cannot set server version')
      return jsonify(status='OK')
    except:
      import traceback
      log.error(traceback.format_exc())
      return make_error_response(500, 'Failed to set version')

  return app
