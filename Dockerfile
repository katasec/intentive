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
        -p:RuntimeIdentifier=linux-musl-arm64; \
    else \
        dotnet publish src/Intentive.Console/Intentive.Console.csproj -c Release -o /app/publish \
        --self-contained true --runtime linux-musl-x64 \
        -p:RuntimeIdentifier=linux-musl-x64; \
    fi

# Runtime stage - .NET runtime on Alpine
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS runtime
WORKDIR /app

# Create app user
RUN adduser -D -H -s /sbin/nologin appuser

# Copy published application and models
COPY --from=build /app/publish/ ./
COPY models/ ./models/

# Set permissions and ownership
RUN chown -R appuser:appuser /app
USER appuser

# Minimal environment
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "Intentive.Console.dll"]
