# Shamir's Secret Sharing Web API

A RESTful API for performing Shamir's Secret Sharing ceremonies with real-time progress updates via SignalR.

## Features

- **RESTful Endpoints**: Create shares and reconstruct secrets via HTTP API
- **Real-time Updates**: SignalR hub for live ceremony progress
- **Health Checks**: Built-in health monitoring endpoints
- **Metrics**: Prometheus metrics for monitoring and alerting
- **Structured Logging**: JSON logs with Loki integration
- **Session Management**: Persistent session state with MongoDB

## Prerequisites

- .NET 8.0 or later
- MongoDB for session storage
- Optional: Grafana Loki for log aggregation
- Optional: Prometheus for metrics collection

## Configuration

Configure the application using `appsettings.json`:

```json
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "shamir_ceremony",
    "CollectionName": "ceremonies"
  },
  "Security": {
    "MinPasswordLength": 12,
    "KdfIterations": 100000,
    "AuditLogEnabled": true
  },
  "FileSystem": {
    "OutputFolder": "./output"
  },
  "Organization": {
    "Name": "Your Organization",
    "ContactPhone": "+1-555-0123"
  }
}
```

## API Endpoints

### Ceremony Operations

#### Create Secret Shares
```http
POST /api/ceremony/create-shares
Content-Type: application/json

{
  "threshold": 3,
  "totalShares": 5,
  "masterSecret": "your-secret-here",
  "keepers": [
    {
      "name": "Alice",
      "email": "alice@example.com",
      "password": "secure-password"
    }
  ]
}
```

#### Reconstruct Secret
```http
POST /api/ceremony/reconstruct-secret
Content-Type: application/json

{
  "sharesFilePath": "/path/to/shares.json",
  "keeperPasswords": {
    "keeper1": "password1",
    "keeper2": "password2"
  }
}
```

#### Get Session Status
```http
GET /api/ceremony/session/{sessionId}
```

### Health and Monitoring

#### Health Check
```http
GET /health
```

#### Readiness Check
```http
GET /health/ready
```

#### Prometheus Metrics
```http
GET /metrics
```

## SignalR Hub

Connect to `/ceremonyhub` for real-time updates:

- `ProgressUpdate`: Ceremony progress notifications
- `ValidationResult`: Validation status updates
- `CompletionNotification`: Ceremony completion events

### JavaScript Client Example

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/ceremonyhub")
    .build();

connection.on("ProgressUpdate", (data) => {
    console.log(`Progress: ${data.percentComplete}% - ${data.message}`);
});

await connection.start();
```

## Running the Application

### Development
```bash
cd src/CSharp/Shamir.Ceremony.Web.Api
dotnet run
```

### Docker
```bash
docker build -t shamir-web-api .
docker run -p 8080:8080 shamir-web-api
```

### Docker Compose
```bash
# From repository root
docker-compose up web-api
```

## Monitoring and Observability

### Metrics

The API exposes Prometheus metrics:
- `ceremonies_total`: Total ceremony count by type and result
- `ceremony_duration_seconds`: Ceremony duration histograms
- `active_sessions`: Current active session count

### Logging

Structured logs are sent to:
- Console (development)
- File (`logs/webapi-{date}.log`)
- Grafana Loki (if configured)

### Health Checks

- `/health`: Basic application health
- `/health/ready`: Readiness for traffic (checks MongoDB and file system)

## Security

- **Input Validation**: FluentValidation for all configuration
- **Secure Headers**: Standard security headers applied
- **Audit Logging**: All operations logged for compliance
- **Session Isolation**: Each ceremony runs in isolated session

## Error Handling

- Comprehensive exception logging
- Graceful error responses
- Automatic session cleanup on failures
- Detailed error context in logs

## Development

### Adding New Endpoints

1. Create controller in `Controllers/`
2. Add request/response models in `Models/`
3. Update `CeremonyService` for business logic
4. Add validation rules if needed

### Testing

```bash
dotnet test
```

### API Documentation

Swagger UI available at `/swagger` in development mode.
