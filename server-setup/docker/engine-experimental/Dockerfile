FROM engine:stable
LABEL MAINTAINER "arin@ifunfactory.com"

RUN funapi_select_repo experimental && \
    funapi_select_repo && \
    apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y funapi1-dev
