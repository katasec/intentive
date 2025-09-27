# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy source code
COPY src/ src/

# Restore and publish the console application directly (skip solution with tests)
RUN dotnet restore src/Intentive.Console/Intentive.Console.csproj
# Determine target architecture and set runtime identifier
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        dotnet publish src/Intentive.Console/Intentive.Console.csproj -c Release -o /app/publish \
        --self-contained true --runtime linux-musl-arm64 \
        -p:PublishSingleFile=true -p:PublishTrimmed=true; \
    else \
        dotnet publish src/Intentive.Console/Intentive.Console.csproj -c Release -o /app/publish \
        --self-contained true --runtime linux-musl-x64 \
        -p:PublishSingleFile=true -p:PublishTrimmed=true; \
    fi

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
