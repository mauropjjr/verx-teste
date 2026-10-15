# syntax=docker/dockerfile:1
# Build: docker build -f src/Banco.Worker.Consolidado/Dockerfile .   (contexto = raiz: Directory.*.props e projetos compartilhados)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada própria: só muda quando props/csproj mudam.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Banco.Dominio/Banco.Dominio.csproj src/Banco.Dominio/
COPY src/Banco.Aplicacao/Banco.Aplicacao.csproj src/Banco.Aplicacao/
COPY src/Banco.Infraestrutura/Banco.Infraestrutura.csproj src/Banco.Infraestrutura/
COPY src/Banco.Worker.Consolidado/Banco.Worker.Consolidado.csproj src/Banco.Worker.Consolidado/
RUN dotnet restore src/Banco.Worker.Consolidado/Banco.Worker.Consolidado.csproj

COPY src/Banco.Dominio/ src/Banco.Dominio/
COPY src/Banco.Aplicacao/ src/Banco.Aplicacao/
COPY src/Banco.Infraestrutura/ src/Banco.Infraestrutura/
COPY src/Banco.Worker.Consolidado/ src/Banco.Worker.Consolidado/
RUN dotnet publish src/Banco.Worker.Consolidado/Banco.Worker.Consolidado.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Imagem aspnet: a Infraestrutura compartilhada referencia o framework ASP.NET Core (padrões de host das Apis).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Usuário não-root embutido na imagem aspnet (UID 1654).
USER $APP_UID
# Sem porta HTTP: o healthcheck confere a idade do batimento gravado enquanto Postgres e o consumidor estão de pé.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
    CMD ["dotnet", "Banco.Worker.Consolidado.dll", "--verificar-saude"]
ENTRYPOINT ["dotnet", "Banco.Worker.Consolidado.dll"]
