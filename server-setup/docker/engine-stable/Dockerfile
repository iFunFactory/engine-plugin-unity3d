FROM ubuntu:16.04
LABEL MAINTAINER "arin@ifunfactory.com"

RUN apt-get update && \
    apt-get install -y wget apt-transport-https && \
    apt-get install -y lsb net-tools gettext dos2unix vim

RUN wget -P /tmp/ https://ifunfactory.com/engine/funapi-apt-setup.deb && \
    dpkg -i /tmp/funapi-apt-setup.deb

RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y funapi1-dev

ADD account.ilf /etc/ifunfactory/

WORKDIR /home/test
