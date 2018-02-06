FROM ubuntu:16.04
LABEL MAINTAINER "arin@ifunfactory.com"

RUN apt-get update && \
    apt-get install -y python python-pip && \
    pip install Django==1.10 Pillow

WORKDIR /home/announcer/
ADD announcer/ /home/announcer/

CMD python ./manage.py runserver 0.0.0.0:8080
