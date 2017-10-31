FROM ubuntu:16.04
MAINTAINER Eunhee Lee <arin@ifunfactory.com>

RUN apt-get update
RUN apt-get install -y wget lsb apt-transport-https

ADD ifunfactory.gpg.key /root/
RUN apt-key add /root/ifunfactory.gpg.key

ADD ifunfactory.list /etc/apt/sources.list.d/

RUN apt-get update
RUN apt-get install -y funapi-apt-setup
RUN rm /etc/apt/sources.list.d/ifunfactory.list

RUN funapi_select_repo experimental
RUN apt-get install -y funapi1-dev

#ADD account.ilf /etc/ifunfactory/

RUN mkdir -p /home/test-server
