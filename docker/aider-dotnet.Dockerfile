FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG AIDER_VERSION=0.86.2

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        python3 \
        python3-pip \
        python3-venv \
        ripgrep \
    && rm -rf /var/lib/apt/lists/*

RUN python3 -m venv /opt/aider \
    && /opt/aider/bin/pip install --no-cache-dir --upgrade pip \
    && /opt/aider/bin/pip install --no-cache-dir "aider-chat==${AIDER_VERSION}"

ENV AIDER_DOCKER_IMAGE=opencontext-agentbridge-aider-dotnet \
    DOTNET_CLI_HOME=/tmp/agentbridge-dotnet \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    HOME=/tmp/agentbridge-home \
    NUGET_PACKAGES=/tmp/agentbridge-nuget/packages \
    PATH="/opt/aider/bin:${PATH}" \
    XDG_DATA_HOME=/tmp/agentbridge-xdg

WORKDIR /app

ENTRYPOINT ["aider"]
