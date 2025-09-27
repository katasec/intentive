# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY src/ src/
COPY *.sln .
RUN dotnet restore
RUN dotnet publish src/Intentive.Console -c Release -o /app/publish \
    --self-contained true \
    --runtime linux-musl-x64 \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true

# Runtime stage - absolute minimal Alpine
FROM alpine:3.19 AS runtime
WORKDIR /app

# Install only essential dependencies
RUN apk add --no-cache \
    ca-certificates \
    libstdc++ \
    libgcc \
    && adduser -D -H -s /sbin/nologin appuser

# Copy single binary and models
COPY --from=build /app/publish/Intentive.Console ./intentive
COPY models/ ./models/

# Set permissions and ownership
RUN chmod +x intentive && chown -R appuser:appuser /app
USER appuser

# Minimal environment
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["./intentive"]
