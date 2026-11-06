FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Deckloom.Api/Deckloom.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /out .
# Diretorio padrao das chaves do Data Protection, com dono nao-root para o volume nomeado.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
USER app
ENTRYPOINT ["dotnet", "Deckloom.Api.dll"]
