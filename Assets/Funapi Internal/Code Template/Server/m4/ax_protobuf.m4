AC_DEFUN([AX_PROTOBUF],[
  AC_CHECK_LIB([protobuf], [exit], ,
               AC_ERROR([FunAPI requires google protobuf library]))

  AC_PATH_PROG([PROTOC], [protoc],
               AC_ERROR([FunAPI requires protobuf compiler]))
])

