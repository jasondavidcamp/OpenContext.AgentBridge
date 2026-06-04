FROM node:22-bookworm-slim

ARG CLINE_VERSION=3.0.17

RUN npm install -g "cline@${CLINE_VERSION}" \
    && cline --version

ENV CLINE_NO_OPEN_BROWSER=1 \
    HOME=/tmp/agentbridge-home \
    XDG_DATA_HOME=/tmp/agentbridge-xdg

WORKDIR /workspace

ENTRYPOINT ["cline"]
