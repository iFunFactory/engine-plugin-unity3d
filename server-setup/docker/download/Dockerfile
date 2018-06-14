FROM engine:experimental
LABEL MAINTAINER "arin@ifunfactory.com"

RUN funapi_initiator plugin && \
    plugin-source/setup_build_environment --type=makefile

WORKDIR /home/test/plugin-build/debug
RUN make

CMD /home/test/plugin-build/debug/plugin-local -session_message_logging_level=2

ADD add/ /home/test/
