# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2017-2018 iFunFactory Inc. All Rights Reserved.
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


log = logging.getLogger('ServerManagerUnity')

DEV_NULL = open(os.devnull, 'w')


is_windows = platform.system() == 'Windows'


class ServerManagerUnity(fds.ServerManager):
  def __init__(self, **kwargs):
    super(ServerManagerUnity, self).__init__(**kwargs)

    # Unity has debug flag
    self._is_unity_editor = kwargs.get('is_unity_editor', False)

  def spawn_match(self, match_id, port, beacon_port, data,
                  max_ds_uptime_seconds):
    # Spawns dedicated server process with predetermined parameters.
    #   port: tell Unity to open the `port` for clients.
    #   heartbeat: tell Unity about heartbeat interval
    #   FunapiMatchID: the ID for the spawned process.
    #   FunapiManagerServer: RESTful endpoint of the manager service.
    #   -nographics -batchmode: mandatory argument for Unity dedicated server.
    #   args: Additional Arguments from the game server.
    cmd = [self.exe_path] + data['args'] + ['-port=' + str(port)]
    if not self._is_unity_editor:
      cmd += ['-nographics', '-batchmode',]
    cmd += ['-RunDedicatedServer',]
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
