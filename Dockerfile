FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /app

# HTTPS no bind 0.0.0.0:5042 (evita protocolo em texto claro — Sonar docker:S5332). Certificado de desenvolvimento
# gerado na imagem; em ambientes reais o TLS costuma terminar no proxy/load balancer.
RUN mkdir -p /https \
    && dotnet dev-certs https -ep /https/aspnetapp.pfx -p "AuthDockerDevOnly"

ENV ASPNETCORE_Kestrel__Certificates__Default__Password="AuthDockerDevOnly"
ENV ASPNETCORE_Kestrel__Certificates__Default__Path="/https/aspnetapp.pfx"
ENV ASPNETCORE_URLS="https://0.0.0.0:5042"

# Ferramenta EF Core disponível globalmente no container
RUN dotnet tool install --global dotnet-ef --version 10.0.5
ENV PATH="${PATH}:/root/.dotnet/tools"

# (Opcional) melhora estabilidade de file-watch em volume Docker
ENV DOTNET_USE_POLLING_FILE_WATCHER=1

# Porta usada pelo seu compose
EXPOSE 5042

# Em dev, você já monta o volume ./AuthService:/app via docker-compose
CMD ["dotnet", "watch", "run", "--no-launch-profile"]
