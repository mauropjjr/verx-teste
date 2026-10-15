# syntax=docker/dockerfile:1
# Build: docker build -f src/Banco.Api.Identidade/Dockerfile .   (contexto = raiz: Directory.*.props e projetos compartilhados)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada própria: só muda quando props/csproj mudam.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Banco.Dominio/Banco.Dominio.csproj src/Banco.Dominio/
COPY src/Banco.Aplicacao/Banco.Aplicacao.csproj src/Banco.Aplicacao/
COPY src/Banco.Infraestrutura/Banco.Infraestrutura.csproj src/Banco.Infraestrutura/
COPY src/Banco.Api.Identidade/Banco.Api.Identidade.csproj src/Banco.Api.Identidade/
RUN dotnet restore src/Banco.Api.Identidade/Banco.Api.Identidade.csproj

COPY src/Banco.Dominio/ src/Banco.Dominio/
COPY src/Banco.Aplicacao/ src/Banco.Aplicacao/
COPY src/Banco.Infraestrutura/ src/Banco.Infraestrutura/
COPY src/Banco.Api.Identidade/ src/Banco.Api.Identidade/
RUN dotnet publish src/Banco.Api.Identidade/Banco.Api.Identidade.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# Pasta da chave RSA (montada como volume no compose): o volume nomeado herda dono e permissões daqui.
RUN mkdir -p /app/chaves && chown "$APP_UID" /app/chaves && chmod 700 /app/chaves
COPY --from=build /app .
# Usuário não-root embutido na imagem aspnet (UID 1654).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Identidade__Chaves__CaminhoArquivo=/app/chaves/identidade-rsa.pem
EXPOSE 8080
# A imagem aspnet não tem curl/wget: o próprio binário faz o GET em /health/live (VerificacaoSaudeLinhaComando).
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD ["dotnet", "Banco.Api.Identidade.dll", "--verificar-saude"]
ENTRYPOINT ["dotnet", "Banco.Api.Identidade.dll"]
