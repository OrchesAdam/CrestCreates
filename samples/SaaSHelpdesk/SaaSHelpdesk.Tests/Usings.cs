global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Headers;
global using System.Net.Http.Json;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
global using FluentAssertions;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.Extensions.DependencyInjection;
global using Xunit;

// Database-backed classes share the long-lived PostgreSQL test instance with per-class schema
// isolation; the acceptance suite stays deterministic when they run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
