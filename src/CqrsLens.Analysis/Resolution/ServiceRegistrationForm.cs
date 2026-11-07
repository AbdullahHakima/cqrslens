namespace CqrsLens;

/// <summary>The syntactic form a registration was written in.</summary>
internal enum ServiceRegistrationForm
{
    /// <summary><c>AddScoped&lt;I, Impl&gt;()</c>.</summary>
    GenericPair,

    /// <summary><c>AddScoped(typeof(I), typeof(Impl))</c>.</summary>
    TypeOfPair,

    /// <summary><c>AddScoped&lt;I&gt;(sp =&gt; sp.GetService&lt;Impl&gt;())</c>.</summary>
    Factory,

    /// <summary><c>RegisterType&lt;Impl&gt;().As&lt;I&gt;()</c> or the typeof twin.</summary>
    AutofacAs,
}
