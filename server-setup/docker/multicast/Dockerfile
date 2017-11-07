FROM ifunengine:latest
MAINTAINER Eunhee Lee <arin@ifunfactory.com>

ENV VERSION 1.0.0-2443xenial
ENV TARGET plugin-multicast

RUN apt-get install -y zookeeperd

#RUN apt-get install -y --allow-downgrades funapi1-runtime=$VERSION funapi1-dev=$VERSION python-funapi1-dev=$VERSION

WORKDIR /home/test-server
RUN funapi_initiator plugin && mv plugin-source $TARGET

WORKDIR /home/test-server/$TARGET/build
RUN cmake .. && make

ADD MANIFEST.json /home/test-server/$TARGET/src/
ADD entrypoint.sh /home/test-server/$TARGET/build/

ENTRYPOINT /home/test-server/$TARGET/build/entrypoint.sh
