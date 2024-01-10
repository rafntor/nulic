// cpplib_with_nuget.cpp : Defines the functions for the static library.
//

#include <stdio.h>
#include <rapidjson/rapidjson.h>
#include <nlohmann/json.hpp>

#define NLOHMANN_VERSION_STRING RAPIDJSON_STRINGIFY(NLOHMANN_JSON_VERSION_MAJOR.NLOHMANN_JSON_VERSION_MINOR.NLOHMANN_JSON_VERSION_PATCH)

// TODO: This is an example of a library function
void show_versions()
{
  printf("nlohmann_json: %s\n", NLOHMANN_VERSION_STRING);
  printf("rapidjson    : %s\n", RAPIDJSON_VERSION_STRING);
}
