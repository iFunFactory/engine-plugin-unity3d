dnl Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
dnl
dnl This work is confidential and proprietary to Nexon Korea Corporation and
dnl must not be used, disclosed, copied, or distributed without the prior
dnl consent of Nexon Korea Corporation.

dnl Process this file with autoconf to produce a configure script.

dnl Customizable variables are
dnl   /unity_server_NAME : project name

AC_PREREQ(2.67)
AC_INIT(/unity_server_NAME, 0.0.1,)
AC_CONFIG_MACRO_DIR(m4)
AC_CONFIG_AUX_DIR(autoconf-aux)
AC_CONFIG_SRCDIR([src/])
AC_CONFIG_HEADER([config.h])
AC_PREFIX_DEFAULT(["/usr"])

dnl Defines pkgbindir to use pkgbin_SCRIPTS
AC_SUBST([pkgbindir], ["\$(bindir)/$PACKAGE_NAME"])

dnl Defines pkgexecdir to use pkgexec_PROGRAMS
AC_SUBST([pkgexecdir], ["\$(bindir)/$PACKAGE_NAME"])

AC_SUBST([PACKAGE_DEB], [`echo $PACKAGE_NAME | sed -e 's/_/-/g'`])

AM_INIT_AUTOMAKE()

dnl Checks required compilers and tools.
AC_PROG_CXX
AC_PROG_CC
AC_PROG_CPP
DX_INIT_DOXYGEN(/unity_server_NAME, doxygen/doxygen.cfg, documents)

dnl Uses gcc extentions and allows files larger than 2GB.
AC_USE_SYSTEM_EXTENSIONS
AC_SYS_LARGEFILE

dnl Prefers libtool shared libs.
LT_PREREQ([2.2])
LT_INIT([dlopen disable-static])
AC_LIBTOOL_DLOPEN
LT_SYS_DLOPEN_SELF
AC_PROG_LIBTOOL

dnl Checks required functions
AC_CHECK_FUNCS([fdatasync ppoll])

dnl Checks required libraries.
dnl Boost sets own variable for library location.
dnl Thus we add such variable to LIBS in Makefile.am.
AX_BOOST_BASE([1.46], , AC_MSG_ERROR([Boost is required]))
AX_BOOST_FILESYSTEM
AX_BOOST_SYSTEM
AX_BOOST_THREAD
AX_BOOST_UNIT_TEST_FRAMEWORK
AX_FUNAPI([0.0.1], , AC_MSG_ERROR([FunAPI is required]))
AX_WEBSOCKETPP

if test "$ax_cv_boost_filesystem" != "yes"; then
  AC_MSG_ERROR([Boost filesystem is required])
fi
if test "$ax_cv_boost_system" != "yes"; then
  AC_MSG_ERROR([Boost system is rquired])
fi
if test "$ax_cv_boost_thread" != "yes"; then
  AC_MSG_ERROR([Boost thread is required])
fi
if test "$ax_cv_boost_unit_test_framework" != "yes"; then
  AC_MSG_ERROR([Boost Unit_Test_Framework is required])
fi

AX_PTHREAD(, AC_MSG_ERROR([Pthread is required]))
AX_PROTOBUF
AX_CHECK_OPENSSL(, AC_MSG_ERROR([OpenSSL is required]))

dnl AC_CHECK_LIB adds to LIBS if action-found is missing.
AC_CHECK_LIB([gflags], [exit], , AC_MSG_ERROR([gflags is required]))
AC_CHECK_LIB([glog], [exit], , AC_MSG_ERROR([glog is required]))
AC_CHECK_LIB([json_spirit], [exit], , AC_MSG_ERROR([json_spirit is required]))
AC_CHECK_LIB([ltdl], [lt_dlopen], , AC_MSG_ERROR([ltdl is required]))
AC_CHECK_LIB([popt], [poptGetContext], , AC_MSG_ERROR([popt is required]))
AC_CHECK_LIB([protobuf], [memcpy], , AC_MSG_ERROR([protobuf is required]))

dnl Optional features. Don't have to stop even if not exist.
AX_CHECK_COVERAGE
AX_CHECK_DEBUGGING
AX_CHECK_PROFILING

AC_CONFIG_FILES([
Makefile
doxygen/doxygen.conf
])
AC_OUTPUT

