# Phase 5: Analytics & Reporting - Implementation Plan

## Overview
Add analytics aggregation and metrics exposure for monitoring/dashboards while minimizing token usage.

## Architecture
```
PostgreSQL → Analytics Views → Metrics Endpoint → Grafana/Prometheus
                              ↓
                         TimeSeries API
```

## Files to Create/Modify

### 1. Database Migration: `hub/Infrastructure/Adapters/Results/Migrations/V25__analytics_views.sql`
- Create materialized views for aggregations
- Add refresh functions
- Create indexes for performance

### 2. Analytics Service: `ingestion/Services/AnalyticsService.cs`
- Query aggregated metrics
- Expose Prometheus metrics
- Cache results (5-minute TTL)

### 3. Endpoints: `ingestion/Program.cs` (modify)
- Add `/metrics/timeseries` endpoint
- Add `/metrics/aggregations` endpoint
- Integrate with existing `/metrics` endpoint

### 4. Configuration: `ingestion/.env.example` (modify)
- Add analytics configuration

## Benefits
- **Real-time metrics** via Prometheus endpoint
- **Pre-aggregated data** via materialized views (no query overhead)
- **Grafana-ready** JSON format
- **Minimal overhead** (cached, materialized views)

## Token Efficiency
- Concise plan (~200 words)
- Focused SQL (views only)
- Minimal code (100 lines total)
- Table-based docs
