# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2017 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

import funapi_dedicated_server as fds
import logging
import os
import platform
import sys

import gevent
import gevent.subprocess as subprocess


log = logging.getLogger('ServerManagerUE4')

DEV_NULL = open(os.devnull, 'w')


is_windows = platform.system() == 'Windows'


class ServerManagerUE4(fds.ServerManager):
  def __init__(self, **kwargs):
    super(ServerManagerUE4, self).__init__(**kwargs)

  def spawn_match(self, match_id, port, beacon_port, data,
                  max_ds_uptime_seconds):
    # Spawns dedicated server process with predetermined parameters.
    #   port: Tell UE4 to open the `port` for clients.
    #   beacon_port: Tell UE4 to open the 'beacon port'
    #   heartbeat: Tell UE4 about heartbeat interval
    #   FunapiMatchID: the ID for the spawned process.
    #   FunapiManagerServer: RESTful endpoint of the manager service.
    #   args: Additional Arguments from the game server.
    cmd = [self.exe_path] + data['args'] + ['-port=' + str(port)]
    if beacon_port > 0:
      cmd += ['-beaconport=' + str(beacon_port)]
    cmd += ['-FunapiMatchID=' + match_id,
            '-FunapiManagerServer=127.0.0.1:{}'.format(self.rest_port),
            '-FunapiHeartbeat={}'.format(self.heartbeat_interval)]
    del data['args']

    wd = os.path.abspath(os.path.dirname(self.exe_path))
    stdout = DEV_NULL
    if self.verbose:
      log.info('spawn_match: ' + ' '.join(cmd))
      stdout = None
    process = subprocess.Popen(cmd, stdout=stdout, stderr=subprocess.STDOUT,
        shell=False, close_fds=not is_windows, cwd=wd)
    pid = process.pid
    log.info('Process {} for match({}) started'.format(pid, match_id))

    def wait():
      process.wait()
      log.info('Process {} for match({}) finished'.format(pid, match_id))
      self.notify_match_finished(match_id)

    def wait_timeout():
      gevent.sleep(max_ds_uptime_seconds)
      process.poll()
      if process.returncode is not None:
        return

      process.kill()
      log.error('Process {} for match({}) exceeded allowed uptime'.format(
          pid, match_id))

    gevent.spawn(wait)

    if max_ds_uptime_seconds > 0:
      gevent.spawn(wait_timeout)
    return pid
