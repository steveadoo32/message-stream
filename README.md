# Introduction 
MessageStream wraps the new Pipelines API into an easy to use framework for message serialization from generic data sources

# Getting Started
Very simple examples are located in MessageStream.Tests.Simple. That is the fastest and easiest way to get started.
You can also look at the benchmark project.

# TODO
1. The codegen pieces for deserialization do not work if you're interface implementations are declared explicitly.
1. Use Benchmark.NET
1. If your message sizes are greater than the stack size(1MB) your app will crash. This will be fixed in later versions.
1. The generated code is not cached for the whole program session. Everytime you create a new deserializer instance it will regenerate. You could avoid this by reusing the deserializers because they are stateless and just recreating your MessageStream.
1. Build out the README.