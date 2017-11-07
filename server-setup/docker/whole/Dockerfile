FROM ifunengine:latest
MAINTAINER Eunhee Lee <arin@ifunfactory.com>

ENV VERSION 1.0.0-2443xenial
ENV TARGET plugin-whole

#RUN apt-get install -y --allow-downgrades funapi1-runtime=$VERSION funapi1-dev=$VERSION python-funapi1-dev=$VERSION

WORKDIR /home/test-server
RUN funapi_initiator plugin && mv plugin-source $TARGET

WORKDIR /home/test-server/$TARGET/build
RUN cmake .. && make

ADD MANIFEST.json /home/test-server/$TARGET/src/

CMD /home/test-server/$TARGET/build/plugin-local -session_message_logging_level=2
