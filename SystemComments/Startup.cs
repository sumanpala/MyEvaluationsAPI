using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SystemComments.Models.DataBase;

namespace SystemComments
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<APIDataBaseContext>(options =>
            {
                options.UseSqlServer(Configuration.GetConnectionString("MyEvalsConnectionString"));
            });
            services.AddCors();
            services.AddControllers();
            var key = "/DW4PZqv44Yr4Rk=";
            //string clientID = GenerateRandomNumber();
            //string clientSecret = GenerateRandomNumber();
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key))
                };
            });

            services.AddSingleton<IJwtAuth>(new Auth(key));

            //services.AddSwaggerGen(c =>
            //{
            //    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SystemComments", Version = "v1" });
            //});

            services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "JWT Authorization header using the Bearer scheme."
                });
                options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                            }
                        },
                        new string[] {}
                }
            });
            });
            services.AddSingleton(sp =>
            {
                var apiKey = Configuration["AppSettings:SAGEToken"]
              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
             

                // ✅ Create the OpenAI client directly with the API key.
                var openAIClient = new OpenAIClient(apiKey);

                return openAIClient;
              
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SystemComments v1"));
            }

            app.UseHttpsRedirection();           
            app.UseRouting();

            app.UseCors(x => x
               .AllowAnyMethod()
               .AllowAnyHeader()
               .SetIsOriginAllowed(origin => true) // allow any origin
               .AllowCredentials()); // allow credentials

            app.UseAuthentication();

            app.UseAuthorization();            

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        public string GenerateRandomNumber()
        {
            RandomNumberGenerator cryptoRandomDataGenerator = new RNGCryptoServiceProvider();
            byte[] buffer = new byte[11];
            cryptoRandomDataGenerator.GetBytes(buffer);
            string uniq = Convert.ToBase64String(buffer);
            return uniq;
        }
    }
}
