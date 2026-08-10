# 3.23 for CMAKE_VS_NUGET_PACKAGE_RESTORE
cmake_minimum_required(VERSION 3.23)

find_package(mo2-cmake CONFIG REQUIRED)
find_package(mo2-uibase CONFIG REQUIRED)
find_package(spdlog CONFIG REQUIRED)

# Dummy .NET library as VS_PACKAGE_REFERENCES doesn't work on C++/CLI projects yet
# Needs to be declared before cmake_common stuff is included as that polutes the environment and makes C# get compiled as C++
add_library(dummy_cs_project SHARED DummyCSFile.cs)
set_target_properties(dummy_cs_project PROPERTIES
	LINKER_LANGUAGE CSharp
	VS_PACKAGE_REFERENCES "OMODFramework_2.2.2;OMODFramework.Scripting_2.2.2;RtfPipe_1.0.7388.1242"
)

# Only dummy_cs_project uses NuGet (via VS_PACKAGE_REFERENCES above); installer_omod
# references the resulting DLLs by path via VS_DOTNET_REFERENCE_*. CMake >= 4.x emits
# <target>_autogen* helper projects for the VS generator, and those inherit the global
# CMAKE_DOTNET_TARGET_FRAMEWORK_VERSION from ../CMakeLists.txt, so MSBuild runs
# Microsoft.NuGet.targets on them. They have no NuGet lock file, so the build fails with
# "Sequence contains no elements". Set after dummy_cs_project so it keeps NuGet.
set(CMAKE_VS_GLOBALS "ResolveNuGetPackages=false")

add_library(installer_omod SHARED)
mo2_configure_plugin(installer_omod WARNINGS 4 CLI ON WERROR ON)
# spdlog::spdlog, NOT spdlog::spdlog_header_only -- the distinction is the whole fix.
# uibase's public log.h forward-declares spdlog::logger while holding a
# std::unique_ptr<spdlog::logger>, and /clr cannot put an incomplete native type into
# managed metadata (LNK4248). Completing the type needs spdlog's headers, but the
# header-only target additionally pulls the *-inl.h implementation headers, which use
# thread_local -- illegal in managed code (C2482/C2483). The compiled-lib target
# defines SPDLOG_COMPILED_LIB, so logger.h stops at the class definition. Layout is
# identical between the two modes: logger's members vary only on
# SPDLOG_USE_STD_FORMAT (both define it) and SPDLOG_WCHAR_TO_UTF8_SUPPORT (neither).
target_link_libraries(installer_omod PRIVATE mo2::uibase spdlog::spdlog)

# I'd like to use get_target_property(source_files ${PROJECT_NAME} SOURCES) as
# globbing is naughty, but need to filter out the things that aren't relative to this directory.
file(GLOB_RECURSE source_files CONFIGURE_DEPENDS *.cpp;*.h;*.ui)
source_group(TREE ${CMAKE_CURRENT_SOURCE_DIR} PREFIX src FILES ${source_files})

# Ideally we'd use "$<TARGET_FILE_DIR:dummy_cs_project>", but the relevant property
# doesn't support generator expressions.
set(omod_framework_prefix "${CMAKE_BINARY_DIR}/$(Configuration)"
	CACHE PATH "Path where OMODFramework.dll and OMODFramework.Scripting.dll can be found. The default value is where the nuget package artifacts will end up. Override if you want to use a custom build.")

set_target_properties(${PROJECT_NAME} PROPERTIES
	COMMON_LANGUAGE_RUNTIME ""
	# C++20 is a hard ceiling under /clr, not a preference: cl.exe rejects anything
	# newer with "warning C4857: C++/CLI mode does not support C++ versions newer
	# than C++20; setting language to /std:c++20" -- it downgrades silently and adds
	# a warning. Do NOT raise this to 23 to match the rest of the tree.
	CXX_STANDARD 20
	#DOTNET_TARGET_FRAMEWORK "netstandard2.0"
	VS_DOTNET_REFERENCE_OMODFramework "${omod_framework_prefix}/OMODFramework.dll"
	VS_DOTNET_REFERENCE_OMODFramework.Scripting "${omod_framework_prefix}/OMODFramework.Scripting.dll"
	VS_DOTNET_REFERENCE_RtfPipe "${CMAKE_BINARY_DIR}/$(Configuration)/RtfPipe.dll"
	VS_DOTNET_REFERENCES "System.dll;System.Core.dll"
)

target_compile_options(
	installer_omod
	PRIVATE
	# OMODFramework and OMODFramework.Scripting reference different .NET standard libraries. This generates warnings when using them together.
	"/wd4691"
	# Qt forward-declares QThread, and under /clr an incomplete native type reaching
	# managed metadata emits "LNK4248: unresolved typeref token ... image may not run"
	# in every TU that touches it. Force the complete definition in; a plain include
	# in the three affected sources would have to be repeated for each new one.
	"/FIQThread"
	# Same story for spdlog::logger, reached through uibase's public log.h.
	"/FIspdlog/logger.h")

# We don't want ERROR defined
target_compile_definitions(installer_omod PRIVATE "NOGDI")

# We don't need to actually link with dummy_cs_project, especially as its dependencies
# aren't pulled in. We do need it to build first, though.
add_dependencies(installer_omod dummy_cs_project)

mo2_install_plugin(installer_omod FOLDER)

install(
	FILES
	"$<TARGET_FILE_DIR:${PROJECT_NAME}>/OMODFramework.dll"
	"$<TARGET_FILE_DIR:${PROJECT_NAME}>/OMODFramework.Scripting.dll"
	"$<TARGET_FILE_DIR:${PROJECT_NAME}>/ICSharpCode.SharpZipLib.dll"
	"$<TARGET_FILE_DIR:${PROJECT_NAME}>/System.Drawing.Common.dll"
	"$<TARGET_FILE_DIR:${PROJECT_NAME}>/RtfPipe.dll"
	DESTINATION bin/plugins/installer_omod/
)
install(
	FILES "$<TARGET_PDB_FILE_DIR:${PROJECT_NAME}>/ICSharpCode.SharpZipLib.pdb"
	DESTINATION pdb)
# Other PDB files get downloaded by Visual Studio during debugging when https://symbols.nuget.org/download/symbols is in the symbol server list
