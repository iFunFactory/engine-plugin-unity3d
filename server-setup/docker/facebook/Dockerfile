FROM engine:experimental
LABEL MAINTAINER "sypark@ifunfactory.com"

RUN funapi_initiator plugin
ADD add/ /home/test/

RUN plugin-source/setup_build_environment --type=makefile
WORKDIR /home/test/plugin-build/debug
RUN make -j2

CMD /home/test/plugin-build/debug/plugin-local -session_message_logging_level=2
