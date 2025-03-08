#!/bin/bash

# Get the git hash
GIT_HASH=$(git rev-parse --short HEAD)

# Create the C# file
cat > "$1" << EOF
namespace WslNotifyd.Constants
{
    internal static class GitInfo
    {
        public const string GitHash = "$GIT_HASH";
    }
}
EOF
