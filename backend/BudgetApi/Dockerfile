# Dockerfile: builds and runs the C# API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BudgetApi/BudgetApi.csproj BudgetApi/
RUN dotnet restore BudgetApi/BudgetApi.csproj
COPY BudgetApi/ BudgetApi/
RUN dotnet publish BudgetApi/BudgetApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BudgetApi.dll"]
