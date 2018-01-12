FROM ifunengine:latest
LABEL MAINTAINER "arin@ifunfactory.com"

ENV TARGET plugin-download

WORKDIR /home/test-server
RUN funapi_initiator plugin && mv plugin-source $TARGET

WORKDIR /home/test-server/$TARGET/build
RUN cmake .. && make

ADD MANIFEST.json /home/test-server/$TARGET/src/
ADD client_data/ /home/test-server/$TARGET/client_data/

CMD /home/test-server/$TARGET/build/plugin-local -session_message_logging_level=2
