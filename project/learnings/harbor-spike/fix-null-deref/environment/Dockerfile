FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 NO_COLOR=1
COPY repo /app
WORKDIR /app
# The fixture as a commit, so the diff after the agent is the change; packages restored at build so
# the agent and the verifier need no network for NuGet.
RUN git config --global user.email proctor@local && git config --global user.name proctor \
 && git init -q && git add -A && git commit -qm fixture \
 && find . -name '*.csproj' -exec dotnet restore -v q {} \;
