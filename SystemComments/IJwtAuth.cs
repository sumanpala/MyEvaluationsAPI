using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SystemComments
{
    public interface IJwtAuth
    {
        string Authentication(string clientID, string clientSecret);
    }
}
