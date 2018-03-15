#!/usr/bin/env python
# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2018 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

import funapi_dedicated_server as fds

import datetime
import logging
import os
import platform
import sys
import urllib
import urlparse

import gevent
import gevent.ssl
import gevent.subprocess as subprocess
from gevent.pywsgi import WSGIServer
import gflags
import netifaces
import requests


is_windows = platform.system() == 'Windows'


log = logging.getLogger('funapi_dedicatd_server')

FLAGS = gflags.FLAGS

gflags.DEFINE_string('redis_host', '127.0.0.1', 'redis server host')
gflags.DEFINE_integer('redis_port', 6379, 'redis server port')
gflags.DEFINE_string('binary_path', '', 'path to dedicated server binary')

gflags.DEFINE_integer('port', 5000, 'listen port for RESTful API')
gflags.DEFINE_boolean('use_beacon', False, 'use beacon port (or not) [UE4]')
gflags.DEFINE_integer('heartbeat', 10, 'heartbeat interval')
gflags.DEFINE_boolean('verbose', False, 'set verbose output')
gflags.DEFINE_integer('max_servers_for_ds_host', 1, 'maximum number of concurrent dedicated servers in this instance')
gflags.DEFINE_integer('max_ds_uptime_seconds', 0,
    'Maximum number of seconds each dedicated server is allowed to run. 0 for no limit')

gflags.DEFINE_boolean('enable_oauth', False, 'Use OAuth for authentication')
gflags.DEFINE_string('oauth_base_url', '', 'Base URL for OAuth service')
gflags.DEFINE_string('oauth_verification_url',
    'iam/oauth/info',
    'URL to verify an OAuth token ({oauth_base_url}/{oauth_verfication_url})')
gflags.DEFINE_string('oauth_auth_url',
    'iam/oauth/token?grant_type=client_credentials',
    'URL to get an OAuth token ({oauth_base_url/{oauth_auth_url})')
gflags.DEFINE_string('oauth_client_id', '', 'OAuth client ID')
gflags.DEFINE_string('oauth_client_secret', '', 'OAuth client secret')
gflags.DEFINE_string('oauth_content_type', 'application/json',
    'Content-Type value which would be sent for access-token request')
gflags.DEFINE_string('state_service_endpoint', '',
    'API endpoint for dedicated server state service')
gflags.DEFINE_string('oauth_verification_public_key', '',
    'Path to RSA public key .pem, which can verify an OAuth token (JWT token)')

gflags.DEFINE_string('engine_type', 'ue4',
    ('Dedicated server engine type: possible values are '
     '`ue4` for Unreal Engine 4 and `unity` for Unity3d.'))

gflags.DEFINE_bool('run_as_unity_editor', False,
    'Assume that the executable is an Unity Editor')

gflags.DEFINE_string('dsm_api_url', '',
    'Public URL for dedicated server manager service')
gflags.DEFINE_string('external_url', '',
    'Externally visible URL for the dedicated server host')
gflags.DEFINE_string('region', '', 'For testing purpose only; DO NOT SET')

# TLS
gflags.DEFINE_bool('enable_tls', False, 'Enable TLS')
gflags.DEFINE_string('tls_cert_file_path', '',
    'Path to .pem filr for the TLS cert')
gflags.DEFINE_string('tls_key_file_path', '',
    'Path to .pem filr for the TLS private key')
gflags.DEFINE_string('tls_ciphers', 'EECDH+AES128:RSA+AES128:!3DES:!MD5',
    'Prefered cipher list for TLS')


if not is_windows:
  gflags.DEFINE_string(
      'restful_interface', '', 'network interface for RESTful APIs')

  gflags.DEFINE_string(
      'game_interface', '', 'network interface for game-service')
else:
  gflags.DEFINE_string(
      'restful_ip', '127.0.0.1', 'network address for RESTful APIs')
  gflags.DEFINE_string(
      'game_ip', '127.0.0.1', 'network address for game-service')


# NIC -> ip address
nic_prefix_ranks = {
  "eno": 0,
  "ens": 1,
  "enp": 2,
  "enx": 3,
  "eth": 4,
}


def sort_key(nic):
  return nic_prefix_ranks[nic[:3]], nic[3:]


def get_nic_list():
  nic_list = []
  for nic in netifaces.interfaces():
    for prefix in nic_prefix_ranks.iterkeys():
      if nic.startswith(prefix):
        nic_list.append(nic)
        break
  return list(sorted(nic_list, key=sort_key))


def is_nic_exists(nic_name):
  return nic_name == 'aws' or nic_name in netifaces.interfaces()


def get_ec2_public_ipv4():
  try:
    return requests.get(
        'http://169.254.169.254/latest/meta-data/public-ipv4').content.strip()
  except:
    log.error('Cannot get IPv4 address from AWS EC2 metadata API')
    sys.exit(1)


def get_nic_address(nic):
  if nic == 'aws':
    return get_ec2_public_ipv4()
  else:
    addresses = netifaces.ifaddresses(nic)
    return addresses[netifaces.AF_INET][0]['addr']


FLAGS(sys.argv)

if not FLAGS.binary_path or not os.path.exists(FLAGS.binary_path):
  log.error('Cannot find binary executable for dedicated server: {}'.format(
      FLAGS.binary_path or '(not specified)'))
  sys.exit(1)


# check for interfaces to bind
# - last nic in list -> game interface
# - first nic in list -> restful API interface
nic_list = get_nic_list()


if not is_windows:
  if not FLAGS.restful_interface:
    log.warn('REST API interface is not given; using ' + nic_list[0])
    FLAGS.restful_interface = nic_list[0]

  if not FLAGS.game_interface:
    log.warn('Game interface is not given; using ' + nic_list[-1])
    FLAGS.game_interface = nic_list[-1]

  if not is_nic_exists(FLAGS.restful_interface):
    log.error('REST API interface ' + FLAGS.restful_interface + ' is not valid')
    sys.exit(1)

  if not is_nic_exists(FLAGS.game_interface):
    log.error('Game interface ' + FLAGS.game_interface + ' is not valid')
    sys.exit(1)

  # FIXME(jinuk): If the host machine is a EC2 instance with multiple NICs,
  #               we should select the appropriate interface.
  restful_host = get_nic_address(FLAGS.restful_interface)
  if FLAGS.restful_interface != 'aws':
    _restful_host = restful_host
  else:
    log.warn('Opening dedicated server manager host to a public IP address is '
             'not a good idea. Please consider using private IP address '
             'or VPN')
    log.warn('You should enforce an appripriate security group on RESTful API '
             'port')
    _restful_host = '0.0.0.0'  # FIXME(jinuk): It's not secure
  game_host = get_nic_address(FLAGS.game_interface)
else:
  # For Windows
  if FLAGS.restful_ip == 'aws':
    log.warn('Opening dedicated server manager host to a public IP address is '
             'not a good idea. Please consider using private IP address '
             'or VPN')
    log.warn('You should enforce an appripriate security group on RESTful API '
             'port')
    restful_host = get_ec2_public_ipv4()
    _restful_host = '0.0.0.0'
  else:
    restful_host = FLAGS.restful_ip
    _restful_host = restful_host

  if FLAGS.game_ip == 'aws':
    game_host = get_ec2_public_ipv4()
  else:
    game_host = FLAGS.game_ip

log.info('REST API: listening on ' + restful_host + ':' + str(FLAGS.port))
log.info('Game clients will be connected to ' + game_host + ':*')

INSTANCE_ID_URL = 'http://169.254.169.254/latest/meta-data/instance-id'
REGION_URL = ('http://169.254.169.254/latest/meta-data/placement/'
              'availability-zone')
instance_id_from_aws = ''
region = FLAGS.region

if not is_windows and FLAGS.game_interface == 'aws':
  instance_id_from_aws = urllib.urlopen(INSTANCE_ID_URL).read()
  region = urllib.urlopen(REGION_URL).read()
elif is_windows and FLAGS.game_ip == 'aws':
  instance_id_from_aws = urllib.urlopen(INSTANCE_ID_URL).read()
  region = urllib.urlopen(REGION_URL).read()

verification_url = ''

if FLAGS.enable_oauth:
  log.info('OAuth enabled')

  if not FLAGS.oauth_verification_url:
    log.error('You must provide OAuth token verification URL to enable OAuth')
    sys.exit(1)

  if not FLAGS.oauth_auth_url:
    log.error('You must provide OAuth authentication URL to enable OAuth')
    sys.exit(1)

  if not FLAGS.oauth_base_url:
    # oauth_auth AND oauth_verification_url MUST be full URL.
    def verify_url(url):
      split = urlparse.urlsplit(url)
      return split.scheme and split.netloc

    if not verify_url(FLAGS.oauth_auth_url):
      log.error('You must provide OAuth base URL to enable OAuth')
      sys.exit(1)

    if not verify_url(FLAGS.oauth_verification_url):
      log.error('You must provide OAuth base URL to enable OAuth')
      sys.exit(1)

    auth_url = FLAGS.oauth_auth_url
    verification_url = FLAGS.oauth_verification_url
  else:
    auth_url = urlparse.urljoin(FLAGS.oauth_base_url, FLAGS.oauth_auth_url)
    verification_url = urlparse.urljoin(FLAGS.oauth_base_url,
        FLAGS.oauth_verification_url)

  if not FLAGS.state_service_endpoint:
    log.error('You must provide dedicated server state service URL')
    sys.exit(1)

  state_proxy = fds.StateApiProxy(
      auth_url,
      FLAGS.oauth_client_id,
      FLAGS.oauth_client_secret,
      FLAGS.oauth_content_type,
      FLAGS.state_service_endpoint,
      )
else:
  state_proxy = fds.RedisStateProxy(redis_host=FLAGS.redis_host,
      redis_port=FLAGS.redis_port)

if FLAGS.engine_type == 'ue4':
  from .ue4_manager import ServerManagerUE4
  log.info('Initializing dedicated server manager for UnrealEngine 4')
  fds.manager = ServerManagerUE4(game_ip=game_host, rest_ip=restful_host,
      rest_port=FLAGS.port, exe_path=FLAGS.binary_path,
      use_beacon=FLAGS.use_beacon, heartbeat=FLAGS.heartbeat,
      verbose=FLAGS.verbose, concurrent_servers=FLAGS.max_servers_for_ds_host,
      instance_id=instance_id_from_aws,
      region=region,
      max_ds_uptime_seconds=FLAGS.max_ds_uptime_seconds,
      engine_url=FLAGS.dsm_api_url,
      external_url=FLAGS.external_url,
      state_proxy=state_proxy)
elif FLAGS.engine_type == 'unity':
  from .unity_manager import ServerManagerUnity
  log.info('Initializing dedicated server manager for Unity')
  fds.manager = ServerManagerUnity(game_ip=game_host, rest_ip=restful_host,
      rest_port=FLAGS.port, exe_path=FLAGS.binary_path,
      use_beacon=False, heartbeat=FLAGS.heartbeat,
      verbose=FLAGS.verbose, concurrent_servers=FLAGS.max_servers_for_ds_host,
      instance_id=instance_id_from_aws,
      region=region,
      max_ds_uptime_seconds=FLAGS.max_ds_uptime_seconds,
      engine_url=FLAGS.dsm_api_url,
      external_url=FLAGS.external_url,
      state_proxy=state_proxy,
      is_unity_editor=FLAGS.run_as_unity_editor)
else:
  log.error(u'Unknown engine type: {}'.format(FLAGS.engine_type))
  sys.exit(2)


if _restful_host == '0.0.0.0':
  addresses = ['0.0.0.0',]
else:
  addresses = list(set(['127.0.0.1', _restful_host]))
_servers = []


verifier = None
if FLAGS.oauth_verification_public_key:
  # NOTE(jinuk): python-cryptography 의존성을 줄이기 위해 여기서 import
  # JWT 확인하는 부분이 없다면 python-cryptography 없이 동작하게 한다.
  from .signing import VerifierRS256

  sign_verifier = VerifierRS256(
      open(FLAGS.oauth_verification_public_key, 'r').read())

  def validate_exp(claim):
    if 'exp' not in claim:
      return False
    try:
      exp = datetime.datetime.utcfromtimestamp(claim['exp'])
      return datetime.datetime.utcnow() < exp
    except:
      log.error('`exp` in JWT is invalid.')
      return False

  def verify(token):
    return sign_verifier.verify(token, validator=validate_exp)
  verifier = verify


if FLAGS.enable_tls:
  if not os.path.exists(FLAGS.tls_cert_file_path):
    log.fatal(
        "Cannot open the TLS cert file: {0}".format(FLAGS.tls_cert_file_path))
  if not os.path.exists(FLAGS.tls_key_file_path):
    log.fatal(
        "Cannot open the TLS private key: {0}".format(FLAGS.tls_key_file_path))


app = fds.create_app(verification_url if FLAGS.enable_oauth else None,
    jwt_verifier=verifier)

for addr in addresses:
  if addr == '127.0.0.1' or not FLAGS.enable_tls:
    svr = WSGIServer((addr, FLAGS.port), app)
  else:
    svr = WSGIServer(
        (addr, FLAGS.port),
        app,
        server_side=True,
        ciphers=FLAGS.tls_ciphers,
        ssl_version=gevent.ssl.PROTOCOL_TLSv1_2,
        certfile=FLAGS.tls_cert_file_path,
        keyfile=FLAGS.tls_key_file_path)
  svr.reuse_addr = True
  _servers.append(svr)

for svr in _servers[:-1]:
  svr.start()

fds.manager.check_version()
_servers[-1].serve_forever()
