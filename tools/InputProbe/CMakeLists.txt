cmake_minimum_required(VERSION 3.20)

project(WidgetRailInputProbe LANGUAGES CXX)

add_executable(InputProbe main.cpp)
target_compile_features(InputProbe PRIVATE cxx_std_20)
target_compile_definitions(InputProbe PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(InputProbe PRIVATE /W4 /permissive- /EHsc)
target_link_libraries(InputProbe PRIVATE user32 shell32 gdi32)
