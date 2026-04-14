# jobs


## JobWorkerQueue

```aiignore

    using var scope = scopeFactory.CreateScope();

    // Stamp the scoped UserContext with the enqueuing caller's identity so that
    // EF tenant filters apply correctly and audit fields (CreatedBy/UpdatedBy) are set.
    var userContext = scope.ServiceProvider.GetService<UserContext>();
    userContext?.SetJobContext(entry.Value.JobContext);

    var worker = scope.ServiceProvider.GetRequiredKeyedService<IJobWorker>(entry.Value.JobType);


```


```aiignore
What lines 47–54 do:

  1. Line 47 — create a fresh DI scope for this one job. Every scoped service (DbContext, validators, the worker itself) gets a new instance isolated to
  this job execution.
  2. Line 51 — ask the scope for its UserContext. In the real app this is registered as AddScoped<UserContext>(), so the scope creates a fresh one.
  Because there's no HttpContext on a background thread, that fresh instance has TenantId = null and AuthUserId = null — the gap we discussed earlier.
  3. Line 52 — stamp it with the caller's identity that was captured at enqueue time (inside EnqueueJob from BaseService). After this line, the scoped
  UserContext has the correct TenantId and AuthUserId, so EF query filters restrict to the right tenant and audit fields get the right user.
  4. Line 54 — only then resolve the worker. Because BrokenhipDbContext depends on UserContext via the same scope, the context the worker receives
  already has the correct tenant stamped on it.

  ---
  Why userContext is null in the test:

  The test's SingletonScopeFactory wraps a manually-built ServiceCollection that never registers UserContext:

  var services = new ServiceCollection();
  var ctx = GetSqliteContext(true);      // DbContext created directly — not from DI
  services.AddSingleton(ctx);
  services.AddSingleton<SaleValidator>(...);
  // ... workers, etc.
  // ← UserContext never added

  So scope.ServiceProvider.GetService<UserContext>() returns null, and ?.SetJobContext(...) becomes a no-op. The test still passes because
  GetSqliteContext(true) builds a UserContext inline with the test's _authSettings (which has MockTenantId set), so the DbContext the worker uses already
   has the right tenant — just not through the job pipeline.

  To make the test exercise the real stamping path, register UserContext in the test's service collection:

  services.AddSingleton<UserContext>(); // starts empty; JobQueueWorker will stamp it

  But since the DbContext is constructed outside DI in the test, it holds its own UserContext reference that the stamp wouldn't reach anyway. The
  integration tests are currently validating the business outcome (TotalPrice updated correctly) rather than the tenant-stamping path. That path is fully
   exercised in the real app where both UserContext and BrokenhipDbContext are scoped together in the same DI scope.



```