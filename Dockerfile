# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Unholycast_/Unholycast_.csproj", "Unholycast_/"]
RUN dotnet restore "./Unholycast_/Unholycast_.csproj"

COPY . .
WORKDIR "/src/Unholycast_"
RUN dotnet build "./Unholycast_.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet publish "./Unholycast_.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

# Copy .NET app
COPY --from=build /app/publish ./

# Install Python 3.12 and system dependencies
RUN apt-get update && \
    apt-get install -y python3.11 python3.11-dev python3-distutils python3-pip ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Install soco globally
RUN pip3 install soco --break-system-packages

# Set environment variable for pythonnet
ENV PYTHONNET_PYDLL=libpython3.11.so

# Entry point
ENTRYPOINT ["dotnet", "Unholycast_.dll"]
