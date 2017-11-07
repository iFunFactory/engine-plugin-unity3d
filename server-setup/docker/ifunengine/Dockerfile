FROM ubuntu:16.04
MAINTAINER Eunhee Lee <arin@ifunfactory.com>

RUN apt-get update && \
    apt-get install -y wget lsb apt-transport-https

ADD ifunfactory.gpg.key /root/
ADD ifunfactory.list /etc/apt/sources.list.d/

RUN apt-key add /root/ifunfactory.gpg.key && \
    apt-get update && \
    apt-get install -y funapi-apt-setup && \
    rm /etc/apt/sources.list.d/ifunfactory.list && \
    funapi_select_repo experimental && \
    apt-get install -y funapi1-dev && \
    mkdir -p /home/test-server && \
    apt-get install -y vim && \
    rm -rf /var/lib/apt/lists/*

#ADD account.ilf /etc/ifunfactory/
