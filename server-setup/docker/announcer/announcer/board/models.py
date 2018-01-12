from __future__ import unicode_literals

from django.db import models

# Create your models here.
from django.db import models
import hashlib

class announcement(models.Model):
    subject = models.CharField(max_length=50, blank=True)
    message = models.TextField(max_length=2000, blank=True)
    image = models.ImageField(upload_to='', blank=True)
    image_md5 = models.CharField(max_length=32, editable=False)
    link_url = models.URLField(max_length=256, blank=True)
    create_date = models.DateTimeField(auto_now_add=True, auto_now=False)


    def save(self, *args, **kwargs):
        if self.image:
            md5 = hashlib.md5()
            buf = self.image.read()
            md5.update(buf)
            self.image_md5 = md5.hexdigest()

        super(announcement, self).save(*args, **kwargs)


    def ToJSON(self):
        _json = { 'subject': self.subject,
                  'message': self.message,
                  'date': self.create_date.strftime('%Y-%m-%d %H:%M'), }

        if self.image:
            _json['image_url'] = self.image.url
            _json['image_md5'] = self.image_md5

        if self.link_url:
            _json['link_url'] = self.link_url

        return _json
