# vim: fileencoding=utf-8 tabstop=2 softtabstop=2 shiftwidth=2 expandtab
#
# Copyright (C) 2016-2017 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc. and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

import base64
import json
import logging
import traceback

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives.serialization import load_pem_public_key


log = logging.getLogger('funapi_ds_state_service')


def b64decode(buf):
  padding = '=' * ((4 - (len(buf) % 4)) % 4)
  x = base64.urlsafe_b64decode(buf + padding)
  return x


class VerifierRS256(object):
  def __init__(self, key_text):
    self.key = load_pem_public_key(key_text, default_backend())

  def verify(self, token, validator=None):
    try:
      header, payload, signature = str(token).split('.', 3)
    except:
      # token 가 JWT가 아니라면 이렇게 됨
      return False

    try:
      verifier = self.key.verifier(
          b64decode(signature), padding.PKCS1v15(), hashes.SHA256())
      verifier.update(header)
      verifier.update('.')
      verifier.update(payload)
    except:
      log.error('Failed to process signature: ' + traceback.format_exc())
      return False

    try:
      verifier.verify()
      if validator:
        return validator(json.loads(b64decode(payload)))
      return True
    except InvalidSignature:
      return False
    except:
      log.error('Failed to verify signature: ' + traceback.format_exc())
