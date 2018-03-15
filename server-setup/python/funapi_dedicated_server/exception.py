# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2017 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.


class MatchAlreadyCreatedException(Exception):
  '''매치가 이미 생성되어 있는 경우 (Match already created)'''
  pass


class MatchNotFoundException(Exception):
  '''매치가 없는 경우 (Match not found)'''
  pass

class TooManyDedicatedServerException(Exception):
  '''설정된 데디케이티드 서버의 수보다 많은 수를 만드려고 했음 (Exceeding maximum number of dedicated server)'''
  pass
