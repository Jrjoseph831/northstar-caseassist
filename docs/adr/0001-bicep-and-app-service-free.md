# ADR 0001: Bicep and App Service Free for the first Azure slice

Status: Accepted  
Date: 2026-08-16

## Context

The project needs reproducible Azure infrastructure while keeping the idle demo
at approximately zero incremental cost. Container Apps can scale to zero, but a
local-source deployment creates an Azure Container Registry when no registry is
supplied. That registry has a fixed cost and is unnecessary for the first
reference slice.

## Decision

Use Bicep because the target is Azure-only and native type validation is useful.
Deploy the ASP.NET Core API to Linux App Service Free F1 by ZIP package. Keep
`alwaysOn` disabled and use the F1-required 32-bit worker process. Use a
system-assigned identity for Blob access.

Azure SQL, AI Search, and Content Safety remain independently enabled template
options. SQL must use the free offer with automatic pause on limit exhaustion.

## Consequences

- The first API deployment requires no paid image registry.
- Free F1 has cold starts, daily compute limits, and no production SLA.
- SQLite under `/home/data` is an explicit temporary reference substitution
  until the Azure SQL free database is validated and enabled.
- The production mapping remains Container Apps or a paid App Service plan with
  private networking, managed identity database access, and centralized
  monitoring.

## Deployment note

The student subscription accepted the Free F1 resources in East US but reported
the site state as `QuotaExceeded` with a regional VM quota of zero. The
application package was accepted but could not start. No paid upgrade is
authorized. Storage remains usable; compute will move to Container Apps
Consumption after a registry-free/public-image path is available at the final
GitHub publication step, or to another verified free serverless host.
