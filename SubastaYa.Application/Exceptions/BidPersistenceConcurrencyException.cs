using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SubastaYa.Application.Exceptions;

public sealed class BidPersistenceConcurrencyException : Exception
{
    public BidPersistenceConcurrencyException(string message)
        : base(message)
    {
    }

    public BidPersistenceConcurrencyException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
