# Shamir's Secret Sharing Blazor UI

Interactive web interface for Shamir's Secret Sharing ceremonies built with Blazor Server and MudBlazor.

## Features

- **Interactive Ceremonies**: Step-by-step wizard for share creation and reconstruction
- **Real-time Updates**: Live progress tracking via SignalR
- **Modern UI**: Material Design components with MudBlazor
- **Session Management**: Persistent ceremony sessions with history
- **Responsive Design**: Works on desktop and mobile devices

## Prerequisites

- .NET 8.0 or later
- Web API backend running
- Modern web browser with JavaScript enabled

## Configuration

Configure the application in `appsettings.json`:

```json
{
  "ApiSettings": {
    "BaseUrl": "https://localhost:7001",
    "HubUrl": "https://localhost:7001/ceremonyhub"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Running the Application

### Development
```bash
cd src/CSharp/Shamir.Ceremony.Web.UI.Blazer
dotnet run
```

### Docker
```bash
docker build -t shamir-blazor-ui .
docker run -p 8081:8081 shamir-blazor-ui
```

## User Interface

### Main Navigation
- **Create Shares**: Start a new secret sharing ceremony
- **Reconstruct Secret**: Recover a secret from existing shares
- **Session History**: View past ceremony sessions
- **About**: Information about the application

### Create Shares Workflow

1. **Organization Setup**: Enter organization details
2. **Parameters**: Configure threshold and total shares
3. **Master Secret**: Provide the secret to be shared
4. **Keepers**: Set up keeper information and passwords
5. **Review**: Confirm ceremony details
6. **Execution**: Monitor real-time progress
7. **Results**: Download generated shares and summary

### Reconstruct Secret Workflow

1. **Upload Shares**: Provide the shares JSON file
2. **Keeper Authentication**: Enter keeper passwords
3. **Execution**: Monitor reconstruction progress
4. **Results**: View the reconstructed secret

### Session History

- View all past ceremonies
- Filter by date, type, and status
- Download ceremony artifacts
- View detailed session logs

## Components

### Pages
- `CreateShares.razor`: Share creation wizard
- `ReconstructSecret.razor`: Secret reconstruction interface
- `SessionHistory.razor`: Historical ceremony data
- `Index.razor`: Dashboard and overview

### Shared Components
- `CeremonyProgress.razor`: Real-time progress display
- `KeeperForm.razor`: Keeper information input
- `ValidationSummary.razor`: Form validation display

### Services
- `ApiService`: HTTP client for Web API communication
- `SignalRService`: Real-time communication hub
- `SessionStateService`: Client-side session management

## Styling and Theming

The application uses MudBlazor with a custom theme:

```csharp
var theme = new MudTheme()
{
    Palette = new PaletteLight()
    {
        Primary = "#1976d2",
        Secondary = "#424242",
        Success = "#4caf50",
        Warning = "#ff9800",
        Error = "#f44336"
    }
};
```

## Real-time Features

### SignalR Integration
```csharp
await hubConnection.StartAsync();

hubConnection.On<ProgressUpdate>("ProgressUpdate", (update) =>
{
    // Update UI with progress
    StateHasChanged();
});
```

### Progress Tracking
- Visual progress bars
- Step-by-step status updates
- Real-time validation feedback
- Error notifications

## Security Considerations

- **Client-side Validation**: Input validation before API calls
- **Secure Communication**: HTTPS for all API communication
- **Session Security**: Secure session token handling
- **Data Protection**: Sensitive data cleared from browser memory

## Accessibility

- **ARIA Labels**: Screen reader support
- **Keyboard Navigation**: Full keyboard accessibility
- **High Contrast**: Support for high contrast themes
- **Responsive Design**: Mobile and tablet friendly

## Development

### Adding New Pages
1. Create `.razor` file in `Components/Pages/`
2. Add navigation link in `MainLayout.razor`
3. Update routing in `App.razor`

### Custom Components
```csharp
@inherits ComponentBase

<MudCard>
    <MudCardContent>
        @ChildContent
    </MudCardContent>
</MudCard>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

### State Management
```csharp
@inject SessionStateService SessionState

@code {
    protected override async Task OnInitializedAsync()
    {
        await SessionState.LoadSessionAsync();
    }
}
```

## Testing

### Unit Tests
```bash
dotnet test Shamir.Ceremony.Web.UI.Blazer.Tests
```

### Integration Tests
- End-to-end ceremony workflows
- SignalR connection testing
- API integration validation

## Deployment

### Production Build
```bash
dotnet publish -c Release -o ./publish
```

### Docker Deployment
```bash
docker build -t shamir-blazor-ui .
docker run -d -p 8081:8081 shamir-blazor-ui
```

## Troubleshooting

### Common Issues

**SignalR Connection Failed**
- Verify Web API is running
- Check CORS configuration
- Validate hub URL in configuration

**API Calls Failing**
- Confirm API base URL
- Check network connectivity
- Verify API authentication

**UI Not Updating**
- Call `StateHasChanged()` after async operations
- Check component lifecycle methods
- Verify event handler subscriptions

### Logging

Client-side logging is available in browser developer tools:
```javascript
// Enable SignalR logging
hubConnection.configureLogging(signalR.LogLevel.Debug);
```

## Browser Support

- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

## Performance

- **Lazy Loading**: Components loaded on demand
- **Virtual Scrolling**: Efficient large list rendering
- **Caching**: API response caching where appropriate
- **Compression**: Gzip compression enabled
