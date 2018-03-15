#!/usr/bin/env python
# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2018 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

from gevent import monkey
monkey.patch_all()


import json
import logging
import sys
import time

import requests


port = 0
manager_address = ''
match_id = ''
heartbeat_interval = 0

is_version_cmd = False

# mimic the argument format of the UE4
for arg in sys.argv[1:]:
  if arg == '-FunapiVersion':
    is_version_cmd = True
    continue
  k, v = arg.split('=')
  if k == '-port':
    port = int(v)
  elif k == '-FunapiMatchID':
    match_id = v
  elif k == '-FunapiManagerServer':
    manager_address = v
  elif k == '-FunapiHeartbeat':
    heartbeat_interval = int(v)
  else:
    assert False, 'Unknown argument: %s' % arg


manager_port = int(manager_address.split(':')[1])


def get_user_data(match_id):
  uri = 'http://localhost:{}/match/{}/'.format(manager_port, match_id)
  res = requests.get(uri)
  res.raise_for_status()
  return json.loads(res.content)


def post_game_ready(match_id):
  uri = 'http://localhost:{}/match/{}/ready/'.format(
      manager_port, match_id)
  requests.post(uri, headers={'Content-Type': 'application/json'},
      data=json.dumps({}))


def post_game_result(match_id):
  uri = 'http://localhost:{}/match/{}/result/'.format(
      manager_port, match_id)
  requests.post(uri, headers={'Content-Type': 'application/json'},
      data=json.dumps({'foo': 'bar'}))


def post_game_state(match_id):
  uri = 'http://localhost:{}/match/{}/state/'.format(
      manager_port, match_id)
  requests.post(uri, headers={'Content-Type': 'application/json'},
      data=json.dumps({"hello": "world"}))


def post_pending_user(match_id):
  uri = 'http://localhost:{}/match/{}/pending_users/'.format(
      manager_port, match_id)
  res = requests.post(uri, headers={'Content-Type': 'text/plain'}, data='')
  print res.status_code, res.content


def post_user_joined(match_id, uid):
  uri = 'http://localhost:{}/match/{}/joined/'.format(
      manager_port, match_id)
  res = requests.post(uri, headers={'Content-Type': 'application/json'},
      data=json.dumps({'uid': uid}))
  print res.status_code, res.content


def send_callback(match_id, data={}):
  uri = 'http://localhost:{}/match/{}/callback/'.format(
      manager_port, match_id)
  res = requests.post(uri, headers={'Content-Type': 'application/json'}, data=json.dumps(data))
  print res.status_code, res.content


def report_version(version_str):
  uri = 'http://localhost:{}/server/version/'.format(manager_port)
  res = requests.post(uri, headers={'Content-Type': 'application/json'},
      data=json.dumps({'version': version_str}))
  print res.status_code, res.content

logging.basicConfig(level=logging.INFO)
log = logging.getLogger('funapi_dedicated_server')
log.setLevel(logging.INFO)

print "Dummy server spawned"

# Check for version flag
if is_version_cmd:
  report_version('.'.join(str(v) for v in sys.version_info[:3]))
  sys.exit(0)

time.sleep(2)
# Get user data from the dedicated server
data = get_user_data(match_id)
print "Dummy server got user data", json.dumps(data, indent=2)

time.sleep(2)
post_game_ready(match_id)
post_game_state(match_id)


time.sleep(3)
for u in data['data']['users']:
  uid = u['uid']
  post_user_joined(match_id, uid)

time.sleep(5)
for i in xrange(10):
  post_game_state(match_id)
  post_pending_user(match_id)
  time.sleep(5)

time.sleep(10)
send_callback(match_id, {'foo': 'bar', 'hello': 'world', 'answer': 42})

time.sleep(30)
post_game_result(match_id)

time.sleep(600)
sys.exit(0)
