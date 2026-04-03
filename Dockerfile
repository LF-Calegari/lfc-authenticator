FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /app

# Executa como usuário não-root para hardening do container (Sonar docker:S6471).
# A imagem base já fornece o usuário "ubuntu" (UID/GID 1000), compatível com
# volumes montados do host na maioria das distros Linux.
RUN chown ubuntu:ubuntu /app

USER ubuntu

# Ferramenta EF Core disponível globalmente no container
RUN dotnet tool install --global dotnet-ef --version 10.0.5
ENV PATH="${PATH}:/home/ubuntu/.dotnet/tools"

# (Opcional) melhora estabilidade de file-watch em volume Docker
ENV DOTNET_USE_POLLING_FILE_WATCHER=1

# Porta usada pelo seu compose
EXPOSE 5042

# Em dev, você já monta o volume ./AuthService:/app via docker-compose
CMD ["dotnet", "watch", "run", "--no-launch-profile", "--urls", "http://0.0.0.0:5042"]