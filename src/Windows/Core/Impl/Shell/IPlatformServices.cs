﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Microsoft.Common.Core.Shell {
    public interface IPlatformServices {
        /// <summary>
        /// Application top level window handle. Typically used as a parent for native dialogs.
        /// </summary>
        IntPtr ApplicationWindowHandle { get; }
    }
}
