FROM ifunengine:latest
LABEL MAINTAINER "arin@ifunfactory.com"

ENV TARGET plugin-multicast

RUN apt-get update && \
    apt-get install -y zookeeperd

WORKDIR /home/test-server
RUN funapi_initiator plugin && mv plugin-source $TARGET
ADD source/ /home/test-server/$TARGET/

WORKDIR /home/test-server/$TARGET/build
RUN cmake .. && make

ENTRYPOINT /home/test-server/$TARGET/entrypoint.sh
