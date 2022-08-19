#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 12552
EXPOSE 18558

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Akka.ShoppingCart/Akka.ShoppingCart.csproj", "Akka.ShoppingCart/"]
COPY ["Akka.ShoppingCart.Abstraction/Akka.ShoppingCart.Abstraction.csproj", "Akka.ShoppingCart.Abstraction/"]
RUN dotnet restore "Akka.ShoppingCart/Akka.ShoppingCart.csproj"
COPY . .
WORKDIR "/src/Akka.ShoppingCart"
RUN dotnet build "Akka.ShoppingCart.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Akka.ShoppingCart.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Akka.ShoppingCart.dll"]