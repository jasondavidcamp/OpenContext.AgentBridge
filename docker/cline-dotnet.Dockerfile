FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG NODE_VERSION=22.22.3
ARG CLINE_VERSION=3.0.17

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        ripgrep \
        xz-utils \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" \
    | tar -xJ -C /usr/local --strip-components=1 \
    && node --version \
    && npm --version

RUN npm install -g "cline@${CLINE_VERSION}" \
    && cline --version

ENV CLINE_NO_OPEN_BROWSER=1 \
    DOTNET_CLI_HOME=/tmp/agentbridge-dotnet \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    HOME=/tmp/agentbridge-home \
    NUGET_PACKAGES=/tmp/agentbridge-nuget/packages \
    XDG_DATA_HOME=/tmp/agentbridge-xdg

WORKDIR /workspace

ENTRYPOINT ["cline"]
