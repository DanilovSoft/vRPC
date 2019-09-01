FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY *.sln .
COPY Client/*.csproj ./Client/
COPY DynamicMethodsLib/*.csproj ./DynamicMethodsLib/
COPY Server/*.csproj ./Server/
COPY Tests/*.csproj ./Tests/
COPY vRPC/*.csproj ./vRPC/
RUN dotnet restore

# copy everything else and build app
COPY . .

# start tests
WORKDIR /app/Tests
#RUN dotnet test Tests.csproj

# publsh
WORKDIR /app/Server
RUN dotnet publish -c Release -o out


FROM mcr.microsoft.com/dotnet/core/aspnet:2.2 AS runtime
WORKDIR /app
COPY --from=build /app/Server/out ./
ENTRYPOINT ["dotnet", "Server.dll"]

