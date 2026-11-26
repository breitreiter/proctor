FROM python:3.13-alpine
RUN apk add --no-cache bash iputils git
WORKDIR /app
COPY probe.py late.sh /opt/probe/
RUN git init -q . && git -c user.name=p -c user.email=p@p commit -q --allow-empty -m init
