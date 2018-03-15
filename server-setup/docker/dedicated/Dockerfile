FROM engine:experimental
LABEL MAINTAINER "arin@ifunfactory.com"

RUN apt-get update && \
    apt-get install -y zookeeper zookeeperd

RUN apt-get install -y redis-server

RUN funapi_initiator plugin
ADD add/ /home/test/
RUN cp redis.conf /etc/redis/

RUN plugin-source/setup_build_environment --type=makefile
WORKDIR /home/test/plugin-build/debug
RUN make

ENTRYPOINT /home/test/plugin-build/entrypoint.sh
